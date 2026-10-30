/* Divakar.GloveOps — demo data (all fictional) */
USE DivakarGloveOps;
GO
SET NOCOUNT ON;

INSERT dbo.Plant (Code, Name, Country) VALUES
 ('TW-TN', N'Tainan Plant',     N'Taiwan'),
 ('CN-01', N'Jiangsu Plant',    N'China'),
 ('VN-01', N'Binh Duong Plant', N'Vietnam');

INSERT dbo.Product (Sku, Name, Material, Category, PlantId) VALUES
 ('NBR-EX-100',  N'Nitrile Exam Glove, powder-free, 3.5 mil',    'NBR', N'Medical',    3),
 ('NBR-CH-200',  N'Chemo-rated Nitrile Glove, ASTM D6978',       'NBR', N'Medical',    3),
 ('NBR-IN-300',  N'Heavy-duty Nitrile Industrial Glove, 8 mil',  'NBR', N'Industrial', 2),
 ('PVC-FD-100',  N'Vinyl Food-service Glove',                    'PVC', N'Food',       2),
 ('PVC-EX-200',  N'Vinyl Exam Glove, powder-free',               'PVC', N'Medical',    1),
 ('TPE-FD-100',  N'TPE Food Glove, low cost',                    'TPE', N'Food',       2),
 ('CR-NBR-100',  N'Cleanroom Nitrile Glove, ISO Class 5',        'NBR', N'Cleanroom',  1),
 ('LTX-SG-100',  N'Latex Surgical Glove, sterile',               'LATEX', N'Medical',  3);

INSERT dbo.Customer (Name, Country) VALUES
 (N'Nordic Care Supplies',      N'Sweden'),
 (N'Bay Area Dental Group',     N'United States'),
 (N'Rhein Medical GmbH',        N'Germany'),
 (N'Sakura Clean Tech',         N'Japan'),
 (N'Lone Star Food Service',    N'United States'),
 (N'Maple Health Distribution', N'Canada'),
 (N'Outback Lab Supplies',      N'Australia'),
 (N'Iberia Hospital Partners',  N'Spain'),
 (N'Hsinchu Semicon Services',  N'Taiwan'),
 (N'Gulf Pharma Trading',       N'United Arab Emirates');

INSERT dbo.AppUser (UserName, DisplayName, Email, Role, PlantId) VALUES
 ('anna',    N'Anna Lin (Sales)',              'anna@gloveops.demo',    'Sales',        NULL),
 ('brian',   N'Brian Wu (Sales)',              'brian@gloveops.demo',   'Sales',        NULL),
 ('kevin',   N'Kevin Chen (Sales Manager)',    'kevin@gloveops.demo',   'SalesManager', NULL),
 ('mei',     N'Mei Huang (Sales Manager)',     'mei@gloveops.demo',     'SalesManager', NULL),
 ('grace',   N'Grace Tsai (QA)',               'grace@gloveops.demo',   'QA',           NULL),
 ('ming',    N'Ming Lee (Plant Mgr, Tainan)',  'ming@gloveops.demo',    'PlantManager', 1),
 ('wei',     N'Wei Zhang (Plant Mgr, Jiangsu)','wei@gloveops.demo',     'PlantManager', 2),
 ('linh',    N'Linh Nguyen (Plant Mgr, VN)',   'linh@gloveops.demo',    'PlantManager', 3),
 ('divakar', N'Divakar Upadhyay (Admin)',      'divakar@gloveops.demo', 'Admin',        NULL);

/* Flow: step 3 only applies to large requests (>= 10,000 boxes) */
INSERT dbo.FlowStep (FormType, StepNo, StepName, ApproverRole, MinQty) VALUES
 ('SampleRequest', 1, N'Sales Manager review',  'SalesManager', NULL),
 ('SampleRequest', 2, N'QA spec check',         'QA',           NULL),
 ('SampleRequest', 3, N'Plant capacity check',  'PlantManager', 10000);
GO

/* ---- run some requests through the real procedures so every status exists ---- */
SET NOCOUNT ON;
DECLARE @Id INT, @No VARCHAR(20), @Final VARCHAR(20), @Rid INT, @Task INT;
DECLARE @d1 DATE = DATEADD(DAY, 14, GETDATE()), @d2 DATE = DATEADD(DAY, 30, GETDATE());

-- 1. Approved (small: 2 steps)
EXEC dbo.usp_SampleRequest_Create 1, 1, 'M', 200, @d1, N'Trial for Nordic hospital tender', 1, @Id OUTPUT, @No OUTPUT;
EXEC dbo.usp_SampleRequest_Submit @Id, 1;
SELECT @Task = TaskId FROM dbo.ApprovalTask WHERE RequestId = @Id AND Status = 'Pending';
EXEC dbo.usp_ApprovalTask_Act @Task, 3, 'Approve', N'Good customer, go ahead', @Final OUTPUT, @Rid OUTPUT;
SELECT @Task = TaskId FROM dbo.ApprovalTask WHERE RequestId = @Id AND Status = 'Pending';
EXEC dbo.usp_ApprovalTask_Act @Task, 5, 'Approve', N'Spec matches EN 455', @Final OUTPUT, @Rid OUTPUT;

-- 2. Approved (large: 3 steps incl. plant)
EXEC dbo.usp_SampleRequest_Create 3, 2, 'L', 12000, @d2, N'Chemo glove pilot for German oncology wards', 2, @Id OUTPUT, @No OUTPUT;
EXEC dbo.usp_SampleRequest_Submit @Id, 2;
SELECT @Task = TaskId FROM dbo.ApprovalTask WHERE RequestId = @Id AND Status = 'Pending';
EXEC dbo.usp_ApprovalTask_Act @Task, 3, 'Approve', NULL, @Final OUTPUT, @Rid OUTPUT;
SELECT @Task = TaskId FROM dbo.ApprovalTask WHERE RequestId = @Id AND Status = 'Pending';
EXEC dbo.usp_ApprovalTask_Act @Task, 5, 'Approve', N'ASTM D6978 report attached', @Final OUTPUT, @Rid OUTPUT;
SELECT @Task = TaskId FROM dbo.ApprovalTask WHERE RequestId = @Id AND Status = 'Pending';
EXEC dbo.usp_ApprovalTask_Act @Task, 8, 'Approve', N'Line 4 capacity OK in week 47', @Final OUTPUT, @Rid OUTPUT;

-- 3. Waiting for QA
EXEC dbo.usp_SampleRequest_Create 4, 7, 'S', 500, @d1, N'Cleanroom qualification samples', 1, @Id OUTPUT, @No OUTPUT;
EXEC dbo.usp_SampleRequest_Submit @Id, 1;
SELECT @Task = TaskId FROM dbo.ApprovalTask WHERE RequestId = @Id AND Status = 'Pending';
EXEC dbo.usp_ApprovalTask_Act @Task, 3, 'Approve', NULL, @Final OUTPUT, @Rid OUTPUT;

-- 4. Waiting for Sales Manager
EXEC dbo.usp_SampleRequest_Create 5, 4, 'L', 800, @d1, N'Restaurant chain trial', 2, @Id OUTPUT, @No OUTPUT;
EXEC dbo.usp_SampleRequest_Submit @Id, 2;

-- 5. Waiting for plant manager (large)
EXEC dbo.usp_SampleRequest_Create 9, 7, 'M', 15000, @d2, N'Fab expansion — monthly volume trial', 1, @Id OUTPUT, @No OUTPUT;
EXEC dbo.usp_SampleRequest_Submit @Id, 1;
SELECT @Task = TaskId FROM dbo.ApprovalTask WHERE RequestId = @Id AND Status = 'Pending';
EXEC dbo.usp_ApprovalTask_Act @Task, 3, 'Approve', NULL, @Final OUTPUT, @Rid OUTPUT;
SELECT @Task = TaskId FROM dbo.ApprovalTask WHERE RequestId = @Id AND Status = 'Pending';
EXEC dbo.usp_ApprovalTask_Act @Task, 5, 'Approve', N'Particle count spec OK', @Final OUTPUT, @Rid OUTPUT;

-- 6. Sent back to applicant
EXEC dbo.usp_SampleRequest_Create 2, 5, 'XS', 300, @d1, N'Dental clinic samples', 1, @Id OUTPUT, @No OUTPUT;
EXEC dbo.usp_SampleRequest_Submit @Id, 1;
SELECT @Task = TaskId FROM dbo.ApprovalTask WHERE RequestId = @Id AND Status = 'Pending';
EXEC dbo.usp_ApprovalTask_Act @Task, 3, 'SendBack', N'Please confirm the size split (XS/S) with the customer', @Final OUTPUT, @Rid OUTPUT;

-- 7. Rejected
EXEC dbo.usp_SampleRequest_Create 10, 8, 'M', 5000, @d1, N'Latex surgical tender', 2, @Id OUTPUT, @No OUTPUT;
EXEC dbo.usp_SampleRequest_Submit @Id, 2;
SELECT @Task = TaskId FROM dbo.ApprovalTask WHERE RequestId = @Id AND Status = 'Pending';
EXEC dbo.usp_ApprovalTask_Act @Task, 3, 'Reject', N'Credit check failed — talk to finance first', @Final OUTPUT, @Rid OUTPUT;

-- 8. Draft
EXEC dbo.usp_SampleRequest_Create 6, 3, 'XL', 1000, @d2, N'Auto-parts plant trial', 1, @Id OUTPUT, @No OUTPUT;

SELECT Status, COUNT(*) AS Requests FROM dbo.SampleRequest GROUP BY Status;
GO
