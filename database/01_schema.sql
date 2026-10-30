/* Divakar.GloveOps — schema
   Sample-request portal with a BPM-style approval engine.
   Run with: sqlcmd -S DIVAKARLAPTOP\DEVELOPERMSSQL -E -i database\00_run_all.sql */

IF DB_ID('DivakarGloveOps') IS NULL CREATE DATABASE DivakarGloveOps;
GO
USE DivakarGloveOps;
GO

DROP TABLE IF EXISTS dbo.IntegrationLog, dbo.ApprovalHistory, dbo.ApprovalTask, dbo.SampleRequest,
                     dbo.FlowStep, dbo.Delegation, dbo.AppUser, dbo.Product, dbo.Plant, dbo.Customer;
DROP SEQUENCE IF EXISTS dbo.SeqSampleRequest;
CREATE SEQUENCE dbo.SeqSampleRequest AS INT START WITH 1 INCREMENT BY 1;
GO

CREATE TABLE dbo.Plant (
    PlantId   INT IDENTITY PRIMARY KEY,
    Code      VARCHAR(10)   NOT NULL UNIQUE,
    Name      NVARCHAR(100) NOT NULL,
    Country   NVARCHAR(50)  NOT NULL
);

CREATE TABLE dbo.Product (
    ProductId   INT IDENTITY PRIMARY KEY,
    Sku         VARCHAR(30)   NOT NULL UNIQUE,
    Name        NVARCHAR(150) NOT NULL,
    Material    VARCHAR(10)   NOT NULL CHECK (Material IN ('NBR','PVC','TPE','LATEX')),
    Category    NVARCHAR(50)  NOT NULL,          -- Medical, Food, Cleanroom, Industrial
    PlantId     INT NOT NULL REFERENCES dbo.Plant(PlantId),
    RowVer      ROWVERSION                    -- optimistic concurrency (EF Core admin pages)
);

CREATE TABLE dbo.Customer (
    CustomerId INT IDENTITY PRIMARY KEY,
    Name       NVARCHAR(150) NOT NULL,
    Country    NVARCHAR(50)  NOT NULL,
    RowVer     ROWVERSION                     -- optimistic concurrency (EF Core admin pages)
);

CREATE TABLE dbo.AppUser (
    UserId      INT IDENTITY PRIMARY KEY,
    UserName    VARCHAR(50)   NOT NULL UNIQUE,
    DisplayName NVARCHAR(100) NOT NULL,
    Email       VARCHAR(150)  NOT NULL,
    Role        VARCHAR(20)   NOT NULL CHECK (Role IN ('Sales','SalesManager','QA','PlantManager','Admin')),
    PlantId     INT NULL REFERENCES dbo.Plant(PlantId),   -- set for PlantManager
    IsActive    BIT NOT NULL DEFAULT 1
);

/* Delegate approvals while a user is on leave (代理人) */
CREATE TABLE dbo.Delegation (
    DelegationId   INT IDENTITY PRIMARY KEY,
    UserId         INT  NOT NULL REFERENCES dbo.AppUser(UserId),
    DelegateUserId INT  NOT NULL REFERENCES dbo.AppUser(UserId),
    FromDate       DATE NOT NULL,
    ToDate         DATE NOT NULL,
    CONSTRAINT CK_Delegation_Dates CHECK (ToDate >= FromDate),
    CONSTRAINT CK_Delegation_Self  CHECK (UserId <> DelegateUserId)
);

/* Flow definition: data-driven routing. MinQty makes a step conditional. */
CREATE TABLE dbo.FlowStep (
    FormType     VARCHAR(30)   NOT NULL,
    StepNo       INT           NOT NULL,
    StepName     NVARCHAR(100) NOT NULL,
    ApproverRole VARCHAR(20)   NOT NULL,
    MinQty       INT NULL,                 -- step applies only when Qty >= MinQty
    CONSTRAINT PK_FlowStep PRIMARY KEY (FormType, StepNo)
);

CREATE TABLE dbo.SampleRequest (
    RequestId    INT IDENTITY PRIMARY KEY,
    RequestNo    VARCHAR(20)   NOT NULL UNIQUE,      -- SR-2026-00001
    CustomerId   INT NOT NULL REFERENCES dbo.Customer(CustomerId),
    ProductId    INT NOT NULL REFERENCES dbo.Product(ProductId),
    Size         VARCHAR(5)    NOT NULL CHECK (Size IN ('XS','S','M','L','XL')),
    Qty          INT           NOT NULL CHECK (Qty > 0),     -- boxes
    RequiredDate DATE          NOT NULL,
    Purpose      NVARCHAR(500) NULL,
    Status       VARCHAR(20)   NOT NULL DEFAULT 'Draft'
                 CHECK (Status IN ('Draft','InApproval','SentBack','Approved','Rejected')),
    CurrentStep  INT NULL,
    ApplicantId  INT NOT NULL REFERENCES dbo.AppUser(UserId),
    CreatedAt    DATETIME2(0) NOT NULL DEFAULT SYSDATETIME(),
    SubmittedAt  DATETIME2(0) NULL,
    ClosedAt     DATETIME2(0) NULL,
    RowVer       ROWVERSION                               -- optimistic concurrency
);
CREATE INDEX IX_SampleRequest_Status ON dbo.SampleRequest(Status) INCLUDE (RequestNo, CreatedAt);
CREATE INDEX IX_SampleRequest_Applicant ON dbo.SampleRequest(ApplicantId);

CREATE TABLE dbo.ApprovalTask (
    TaskId     INT IDENTITY PRIMARY KEY,
    RequestId  INT NOT NULL REFERENCES dbo.SampleRequest(RequestId),
    StepNo     INT NOT NULL,
    ApproverId INT NOT NULL REFERENCES dbo.AppUser(UserId),
    DelegateId INT NULL REFERENCES dbo.AppUser(UserId),
    Status     VARCHAR(20) NOT NULL DEFAULT 'Pending'
               CHECK (Status IN ('Pending','Approved','Rejected','SentBack','Cancelled')),
    CreatedAt  DATETIME2(0) NOT NULL DEFAULT SYSDATETIME(),
    ActedAt    DATETIME2(0) NULL,
    ActedBy    INT NULL REFERENCES dbo.AppUser(UserId)
);
CREATE INDEX IX_ApprovalTask_Pending ON dbo.ApprovalTask(Status, ApproverId) INCLUDE (RequestId, DelegateId);

/* Audit trail (who did what, when) — required for ISO 13485 style traceability */
CREATE TABLE dbo.ApprovalHistory (
    HistoryId INT IDENTITY PRIMARY KEY,
    RequestId INT NOT NULL REFERENCES dbo.SampleRequest(RequestId),
    StepNo    INT NULL,
    UserId    INT NOT NULL REFERENCES dbo.AppUser(UserId),
    Action    VARCHAR(20) NOT NULL,         -- Created, Submitted, Approved, Rejected, SentBack, Resubmitted
    Comment   NVARCHAR(500) NULL,
    ActedAt   DATETIME2(0) NOT NULL DEFAULT SYSDATETIME()
);
CREATE INDEX IX_ApprovalHistory_Request ON dbo.ApprovalHistory(RequestId);

/* Outbound integrations (n8n webhook) and inbound callbacks */
CREATE TABLE dbo.IntegrationLog (
    LogId     INT IDENTITY PRIMARY KEY,
    RequestId INT NULL REFERENCES dbo.SampleRequest(RequestId),
    Channel   VARCHAR(30)  NOT NULL,        -- n8n-webhook, n8n-callback, dify
    Status    VARCHAR(20)  NOT NULL,        -- Sent, Failed, Skipped, Received
    Detail    NVARCHAR(1000) NULL,
    LoggedAt  DATETIME2(0) NOT NULL DEFAULT SYSDATETIME()
);
GO
