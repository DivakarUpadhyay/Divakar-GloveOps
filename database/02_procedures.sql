/* Divakar.GloveOps — stored procedures
   The approval engine lives here: every state change is one transaction
   with an audit row, so the app layer (Dapper) stays thin. */
USE DivakarGloveOps;
GO

/* ---------- helpers ---------- */

/* Picks the approver for a step: the plant's manager for PlantManager steps,
   otherwise the first active user in the role. Also resolves today's delegate. */
CREATE OR ALTER PROCEDURE dbo.usp_ResolveApprover
    @RequestId  INT,
    @Role       VARCHAR(20),
    @ApproverId INT OUTPUT,
    @DelegateId INT OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET @ApproverId = NULL; SET @DelegateId = NULL;

    IF @Role = 'PlantManager'
        SELECT TOP (1) @ApproverId = u.UserId
        FROM dbo.SampleRequest r
        JOIN dbo.Product p ON p.ProductId = r.ProductId
        JOIN dbo.AppUser u ON u.PlantId = p.PlantId AND u.Role = 'PlantManager' AND u.IsActive = 1
        WHERE r.RequestId = @RequestId
        ORDER BY u.UserId;
    ELSE
        SELECT TOP (1) @ApproverId = UserId
        FROM dbo.AppUser WHERE Role = @Role AND IsActive = 1
        ORDER BY UserId;

    IF @ApproverId IS NULL
        THROW 50010, 'No active approver found for this step.', 1;

    SELECT TOP (1) @DelegateId = DelegateUserId
    FROM dbo.Delegation
    WHERE UserId = @ApproverId AND CAST(SYSDATETIME() AS DATE) BETWEEN FromDate AND ToDate
    ORDER BY DelegationId DESC;
END
GO

/* Creates the task for the next applicable step after @AfterStep.
   Returns @NextStep = NULL when no step is left (flow complete). */
CREATE OR ALTER PROCEDURE dbo.usp_CreateNextTask
    @RequestId INT,
    @AfterStep INT,
    @NextStep  INT OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @Qty INT, @Role VARCHAR(20), @ApproverId INT, @DelegateId INT;
    SET @NextStep = NULL;   -- SELECT below leaves it untouched when no row matches

    SELECT @Qty = Qty FROM dbo.SampleRequest WHERE RequestId = @RequestId;

    SELECT TOP (1) @NextStep = StepNo, @Role = ApproverRole
    FROM dbo.FlowStep
    WHERE FormType = 'SampleRequest'
      AND StepNo > @AfterStep
      AND (MinQty IS NULL OR @Qty >= MinQty)          -- conditional step
    ORDER BY StepNo;

    IF @NextStep IS NULL RETURN;

    EXEC dbo.usp_ResolveApprover @RequestId, @Role, @ApproverId OUTPUT, @DelegateId OUTPUT;

    INSERT dbo.ApprovalTask (RequestId, StepNo, ApproverId, DelegateId)
    VALUES (@RequestId, @NextStep, @ApproverId, @DelegateId);

    UPDATE dbo.SampleRequest SET CurrentStep = @NextStep WHERE RequestId = @RequestId;
END
GO

/* ---------- sample request ---------- */

CREATE OR ALTER PROCEDURE dbo.usp_SampleRequest_Create
    @CustomerId   INT,
    @ProductId    INT,
    @Size         VARCHAR(5),
    @Qty          INT,
    @RequiredDate DATE,
    @Purpose      NVARCHAR(500),
    @ApplicantId  INT,
    @RequestId    INT OUTPUT,
    @RequestNo    VARCHAR(20) OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    BEGIN TRY
        BEGIN TRAN;
            SET @RequestNo = CONCAT('SR-', YEAR(SYSDATETIME()), '-',
                                    RIGHT('00000' + CAST(NEXT VALUE FOR dbo.SeqSampleRequest AS VARCHAR(10)), 5));

            INSERT dbo.SampleRequest (RequestNo, CustomerId, ProductId, Size, Qty, RequiredDate, Purpose, ApplicantId)
            VALUES (@RequestNo, @CustomerId, @ProductId, @Size, @Qty, @RequiredDate, @Purpose, @ApplicantId);
            SET @RequestId = SCOPE_IDENTITY();

            INSERT dbo.ApprovalHistory (RequestId, UserId, Action) VALUES (@RequestId, @ApplicantId, 'Created');
        COMMIT;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK;
        THROW;
    END CATCH
END
GO

CREATE OR ALTER PROCEDURE dbo.usp_SampleRequest_Update
    @RequestId    INT,
    @CustomerId   INT,
    @ProductId    INT,
    @Size         VARCHAR(5),
    @Qty          INT,
    @RequiredDate DATE,
    @Purpose      NVARCHAR(500),
    @UserId       INT
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE dbo.SampleRequest
    SET CustomerId = @CustomerId, ProductId = @ProductId, Size = @Size, Qty = @Qty,
        RequiredDate = @RequiredDate, Purpose = @Purpose
    WHERE RequestId = @RequestId AND ApplicantId = @UserId AND Status IN ('Draft','SentBack');

    IF @@ROWCOUNT = 0
        THROW 50020, 'Only the applicant can edit a Draft or Sent Back request.', 1;
END
GO

CREATE OR ALTER PROCEDURE dbo.usp_SampleRequest_Submit
    @RequestId INT,
    @UserId    INT
AS
BEGIN
    SET NOCOUNT ON;
    BEGIN TRY
        BEGIN TRAN;
            DECLARE @OldStatus VARCHAR(20), @NextStep INT;

            SELECT @OldStatus = Status FROM dbo.SampleRequest WITH (UPDLOCK)
            WHERE RequestId = @RequestId AND ApplicantId = @UserId;

            IF @OldStatus IS NULL OR @OldStatus NOT IN ('Draft','SentBack')
                THROW 50030, 'Only the applicant can submit a Draft or Sent Back request.', 1;

            UPDATE dbo.SampleRequest SET Status = 'InApproval', SubmittedAt = SYSDATETIME()
            WHERE RequestId = @RequestId;

            INSERT dbo.ApprovalHistory (RequestId, UserId, Action)
            VALUES (@RequestId, @UserId, IIF(@OldStatus = 'SentBack', 'Resubmitted', 'Submitted'));

            EXEC dbo.usp_CreateNextTask @RequestId, 0, @NextStep OUTPUT;
        COMMIT;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK;
        THROW;
    END CATCH
END
GO

/* Approve / Reject / SendBack. The applicant's request moves on only when
   exactly one pending task was updated — this blocks double clicks and
   two approvers acting at the same time. */
CREATE OR ALTER PROCEDURE dbo.usp_ApprovalTask_Act
    @TaskId      INT,
    @UserId      INT,
    @Action      VARCHAR(20),          -- Approve | Reject | SendBack
    @Comment     NVARCHAR(500),
    @FinalStatus VARCHAR(20) OUTPUT,   -- request status after the action
    @RequestId   INT OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    IF @Action NOT IN ('Approve','Reject','SendBack')
        THROW 50040, 'Unknown action.', 1;
    IF @Action IN ('Reject','SendBack') AND NULLIF(LTRIM(@Comment), '') IS NULL
        THROW 50041, 'A comment is required to reject or send back.', 1;

    BEGIN TRY
        BEGIN TRAN;
            DECLARE @StepNo INT, @NextStep INT;
            DECLARE @TaskStatus VARCHAR(20) =
                CASE @Action WHEN 'Approve' THEN 'Approved' WHEN 'Reject' THEN 'Rejected' ELSE 'SentBack' END;

            UPDATE dbo.ApprovalTask
            SET Status = @TaskStatus, ActedAt = SYSDATETIME(), ActedBy = @UserId,
                @StepNo = StepNo, @RequestId = RequestId
            WHERE TaskId = @TaskId AND Status = 'Pending'
              AND (ApproverId = @UserId OR DelegateId = @UserId);

            IF @@ROWCOUNT = 0
                THROW 50042, 'This task is not pending for you (already handled or not assigned to you).', 1;

            INSERT dbo.ApprovalHistory (RequestId, StepNo, UserId, Action, Comment)
            VALUES (@RequestId, @StepNo, @UserId, @TaskStatus, @Comment);

            IF @Action = 'Approve'
            BEGIN
                EXEC dbo.usp_CreateNextTask @RequestId, @StepNo, @NextStep OUTPUT;
                IF @NextStep IS NULL
                    UPDATE dbo.SampleRequest SET Status = 'Approved', CurrentStep = NULL, ClosedAt = SYSDATETIME()
                    WHERE RequestId = @RequestId;
            END
            ELSE IF @Action = 'Reject'
                UPDATE dbo.SampleRequest SET Status = 'Rejected', CurrentStep = NULL, ClosedAt = SYSDATETIME()
                WHERE RequestId = @RequestId;
            ELSE
                UPDATE dbo.SampleRequest SET Status = 'SentBack', CurrentStep = NULL
                WHERE RequestId = @RequestId;

            SELECT @FinalStatus = Status FROM dbo.SampleRequest WHERE RequestId = @RequestId;
        COMMIT;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK;
        THROW;
    END CATCH
END
GO

/* ---------- queries ---------- */

CREATE OR ALTER PROCEDURE dbo.usp_SampleRequest_List
    @Status      VARCHAR(20)   = NULL,
    @Search      NVARCHAR(100) = NULL,
    @ApplicantId INT           = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SELECT r.RequestId, r.RequestNo, c.Name AS CustomerName, c.Country, p.Sku, p.Name AS ProductName,
           p.Material, r.Size, r.Qty, r.RequiredDate, r.Status, r.CreatedAt,
           u.DisplayName AS ApplicantName, fs.StepName AS CurrentStepName
    FROM dbo.SampleRequest r
    JOIN dbo.Customer c ON c.CustomerId = r.CustomerId
    JOIN dbo.Product  p ON p.ProductId  = r.ProductId
    JOIN dbo.AppUser  u ON u.UserId     = r.ApplicantId
    LEFT JOIN dbo.FlowStep fs ON fs.FormType = 'SampleRequest' AND fs.StepNo = r.CurrentStep
    WHERE (@Status IS NULL OR r.Status = @Status)
      AND (@ApplicantId IS NULL OR r.ApplicantId = @ApplicantId)
      AND (@Search IS NULL OR r.RequestNo LIKE '%' + @Search + '%'
                           OR c.Name LIKE '%' + @Search + '%'
                           OR p.Name LIKE '%' + @Search + '%')
    ORDER BY r.RequestId DESC;
END
GO

/* Three result sets for the detail page (read with Dapper QueryMultiple) */
CREATE OR ALTER PROCEDURE dbo.usp_SampleRequest_GetDetail
    @RequestId INT
AS
BEGIN
    SET NOCOUNT ON;
    SELECT r.RequestId, r.RequestNo, r.CustomerId, c.Name AS CustomerName, c.Country,
           r.ProductId, p.Sku, p.Name AS ProductName, p.Material, p.Category, pl.Name AS PlantName,
           r.Size, r.Qty, r.RequiredDate, r.Purpose, r.Status, r.CurrentStep, fs.StepName AS CurrentStepName,
           r.ApplicantId, u.DisplayName AS ApplicantName, r.CreatedAt, r.SubmittedAt, r.ClosedAt
    FROM dbo.SampleRequest r
    JOIN dbo.Customer c ON c.CustomerId = r.CustomerId
    JOIN dbo.Product  p ON p.ProductId  = r.ProductId
    JOIN dbo.Plant   pl ON pl.PlantId   = p.PlantId
    JOIN dbo.AppUser  u ON u.UserId     = r.ApplicantId
    LEFT JOIN dbo.FlowStep fs ON fs.FormType = 'SampleRequest' AND fs.StepNo = r.CurrentStep
    WHERE r.RequestId = @RequestId;

    SELECT t.TaskId, t.StepNo, fs.StepName, t.ApproverId, a.DisplayName AS ApproverName,
           t.DelegateId, d.DisplayName AS DelegateName, t.Status, t.CreatedAt, t.ActedAt
    FROM dbo.ApprovalTask t
    JOIN dbo.FlowStep fs ON fs.FormType = 'SampleRequest' AND fs.StepNo = t.StepNo
    JOIN dbo.AppUser a ON a.UserId = t.ApproverId
    LEFT JOIN dbo.AppUser d ON d.UserId = t.DelegateId
    WHERE t.RequestId = @RequestId
    ORDER BY t.TaskId;

    SELECT h.HistoryId, h.StepNo, h.UserId, u.DisplayName AS UserName, h.Action, h.Comment, h.ActedAt
    FROM dbo.ApprovalHistory h
    JOIN dbo.AppUser u ON u.UserId = h.UserId
    WHERE h.RequestId = @RequestId
    ORDER BY h.HistoryId;
END
GO

CREATE OR ALTER PROCEDURE dbo.usp_MyTasks
    @UserId INT
AS
BEGIN
    SET NOCOUNT ON;
    SELECT t.TaskId, t.RequestId, r.RequestNo, c.Name AS CustomerName, p.Name AS ProductName,
           r.Qty, r.RequiredDate, fs.StepName, t.CreatedAt,
           CAST(IIF(t.DelegateId = @UserId AND t.ApproverId <> @UserId, 1, 0) AS BIT) AS IsDelegated,
           a.DisplayName AS ApproverName
    FROM dbo.ApprovalTask t
    JOIN dbo.SampleRequest r ON r.RequestId = t.RequestId
    JOIN dbo.Customer c ON c.CustomerId = r.CustomerId
    JOIN dbo.Product  p ON p.ProductId  = r.ProductId
    JOIN dbo.FlowStep fs ON fs.FormType = 'SampleRequest' AND fs.StepNo = t.StepNo
    JOIN dbo.AppUser  a ON a.UserId = t.ApproverId
    WHERE t.Status = 'Pending' AND (t.ApproverId = @UserId OR t.DelegateId = @UserId)
    ORDER BY t.CreatedAt;
END
GO

CREATE OR ALTER PROCEDURE dbo.usp_Dashboard
AS
BEGIN
    SET NOCOUNT ON;
    -- 1. counts by status
    SELECT Status, COUNT(*) AS Total FROM dbo.SampleRequest GROUP BY Status;

    -- 2. average approval lead time (hours) for closed requests
    SELECT CAST(AVG(DATEDIFF(MINUTE, SubmittedAt, ClosedAt) / 60.0) AS DECIMAL(10,1)) AS AvgLeadHours
    FROM dbo.SampleRequest WHERE Status = 'Approved' AND SubmittedAt IS NOT NULL;

    -- 3. boxes requested by material
    SELECT p.Material, SUM(r.Qty) AS Boxes
    FROM dbo.SampleRequest r JOIN dbo.Product p ON p.ProductId = r.ProductId
    GROUP BY p.Material ORDER BY Boxes DESC;

    -- 4. top countries
    SELECT TOP (5) c.Country, COUNT(*) AS Requests
    FROM dbo.SampleRequest r JOIN dbo.Customer c ON c.CustomerId = r.CustomerId
    GROUP BY c.Country ORDER BY Requests DESC;
END
GO

CREATE OR ALTER PROCEDURE dbo.usp_Delegation_Set
    @UserId         INT,
    @DelegateUserId INT,
    @FromDate       DATE,
    @ToDate         DATE
AS
BEGIN
    SET NOCOUNT ON;
    BEGIN TRY
        BEGIN TRAN;
            INSERT dbo.Delegation (UserId, DelegateUserId, FromDate, ToDate)
            VALUES (@UserId, @DelegateUserId, @FromDate, @ToDate);

            -- hand over tasks that are already waiting, if the leave starts today
            IF CAST(SYSDATETIME() AS DATE) BETWEEN @FromDate AND @ToDate
                UPDATE dbo.ApprovalTask SET DelegateId = @DelegateUserId
                WHERE ApproverId = @UserId AND Status = 'Pending';
        COMMIT;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK;
        THROW;
    END CATCH
END
GO

CREATE OR ALTER PROCEDURE dbo.usp_IntegrationLog_Add
    @RequestId INT = NULL,
    @Channel   VARCHAR(30),
    @Status    VARCHAR(20),
    @Detail    NVARCHAR(1000) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    INSERT dbo.IntegrationLog (RequestId, Channel, Status, Detail)
    VALUES (@RequestId, @Channel, @Status, LEFT(@Detail, 1000));
END
GO
