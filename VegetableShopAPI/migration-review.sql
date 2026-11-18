IF OBJECT_ID(N'[__EFMigrationsHistory]') IS NULL
BEGIN
    CREATE TABLE [__EFMigrationsHistory] (
        [MigrationId] nvarchar(150) NOT NULL,
        [ProductVersion] nvarchar(32) NOT NULL,
        CONSTRAINT [PK___EFMigrationsHistory] PRIMARY KEY ([MigrationId])
    );
END;
GO

BEGIN TRANSACTION;
CREATE TABLE [Categories] (
    [Id] int NOT NULL IDENTITY,
    [Name] nvarchar(max) NOT NULL,
    [Description] nvarchar(max) NULL,
    CONSTRAINT [PK_Categories] PRIMARY KEY ([Id])
);

CREATE TABLE [OrderDetails] (
    [Id] int NOT NULL IDENTITY,
    [OrderId] int NOT NULL,
    [ProductId] int NOT NULL,
    [Quantity] int NOT NULL,
    [Price] decimal(18,2) NOT NULL,
    CONSTRAINT [PK_OrderDetails] PRIMARY KEY ([Id])
);

CREATE TABLE [Orders] (
    [Id] int NOT NULL IDENTITY,
    [UserId] int NOT NULL,
    [OrderDate] datetime2 NOT NULL,
    [TotalPrice] decimal(18,2) NOT NULL,
    [Status] nvarchar(max) NOT NULL,
    [ShippingAddress] nvarchar(max) NOT NULL,
    CONSTRAINT [PK_Orders] PRIMARY KEY ([Id])
);

CREATE TABLE [Products] (
    [Id] int NOT NULL IDENTITY,
    [Name] nvarchar(max) NOT NULL,
    [Price] decimal(18,2) NOT NULL,
    [Quantity] int NOT NULL,
    [Unit] nvarchar(max) NOT NULL,
    [Image] nvarchar(max) NOT NULL,
    [Description] nvarchar(max) NULL,
    [CategoryId] int NOT NULL,
    CONSTRAINT [PK_Products] PRIMARY KEY ([Id])
);

CREATE TABLE [Users] (
    [Id] int NOT NULL IDENTITY,
    [FullName] nvarchar(max) NOT NULL,
    [Email] nvarchar(max) NOT NULL,
    [Password] nvarchar(max) NOT NULL,
    [Role] nvarchar(max) NOT NULL,
    CONSTRAINT [PK_Users] PRIMARY KEY ([Id])
);

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20261015104452_UpdateImageColumn', N'9.0.20');

ALTER TABLE [Users] ADD [Phone] nvarchar(max) NOT NULL DEFAULT N'';

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20261015133043_AddPhoneToUsers', N'9.0.20');

ALTER TABLE [Orders] ADD [RecipientName] nvarchar(max) NOT NULL DEFAULT N'';

ALTER TABLE [Orders] ADD [Phone] nvarchar(max) NOT NULL DEFAULT N'';

ALTER TABLE [Orders] ADD [Note] nvarchar(max) NOT NULL DEFAULT N'';

ALTER TABLE [Orders] ADD [PaymentMethod] nvarchar(max) NOT NULL DEFAULT N'';

ALTER TABLE [Orders] ADD [ShippingFee] decimal(18,2) NULL;

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20261017100000_AddOrderDeliveryDetails', N'9.0.20');

DECLARE @var sysname;
SELECT @var = [d].[name]
FROM [sys].[default_constraints] [d]
INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
WHERE ([d].[parent_object_id] = OBJECT_ID(N'[Users]') AND [c].[name] = N'Phone');
IF @var IS NOT NULL EXEC(N'ALTER TABLE [Users] DROP CONSTRAINT [' + @var + '];');
ALTER TABLE [Users] DROP COLUMN [Phone];

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20261017100211_UpdateDatabaseSchema', N'9.0.20');

CREATE INDEX [IX_Products_CategoryId] ON [Products] ([CategoryId]);

CREATE INDEX [IX_Orders_UserId] ON [Orders] ([UserId]);

CREATE INDEX [IX_OrderDetails_OrderId] ON [OrderDetails] ([OrderId]);

CREATE INDEX [IX_OrderDetails_ProductId] ON [OrderDetails] ([ProductId]);

ALTER TABLE [Products] ADD CONSTRAINT [FK_Products_Categories_CategoryId] FOREIGN KEY ([CategoryId]) REFERENCES [Categories] ([Id]) ON DELETE NO ACTION;

ALTER TABLE [Orders] ADD CONSTRAINT [FK_Orders_Users_UserId] FOREIGN KEY ([UserId]) REFERENCES [Users] ([Id]) ON DELETE NO ACTION;

ALTER TABLE [OrderDetails] ADD CONSTRAINT [FK_OrderDetails_Orders_OrderId] FOREIGN KEY ([OrderId]) REFERENCES [Orders] ([Id]) ON DELETE NO ACTION;

ALTER TABLE [OrderDetails] ADD CONSTRAINT [FK_OrderDetails_Products_ProductId] FOREIGN KEY ([ProductId]) REFERENCES [Products] ([Id]) ON DELETE NO ACTION;

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20261017110000_AddOrderProductForeignKeys', N'9.0.20');

CREATE TABLE [PasswordResetRequests] (
    [Id] uniqueidentifier NOT NULL,
    [UserId] int NOT NULL,
    [EncryptedPassword] nvarchar(2048) NOT NULL,
    [Status] nvarchar(20) NOT NULL,
    [CreatedAtUtc] datetime2 NOT NULL,
    [ReviewedAtUtc] datetime2 NULL,
    CONSTRAINT [PK_PasswordResetRequests] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_PasswordResetRequests_Users_UserId] FOREIGN KEY ([UserId]) REFERENCES [Users] ([Id]) ON DELETE CASCADE
);

CREATE INDEX [IX_PasswordResetRequests_UserId_Status] ON [PasswordResetRequests] ([UserId], [Status]);

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20261103052521_AddPasswordResetRequests', N'9.0.20');

ALTER TABLE [Users] ADD [Phone] nvarchar(max) NOT NULL DEFAULT N'';

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20261111113430_RestorePhoneToUsers', N'9.0.20');

ALTER TABLE [Users] ADD [DateOfBirth] date NULL;

ALTER TABLE [Users] ADD [Gender] nvarchar(max) NOT NULL DEFAULT N'';

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20261111114937_AddGenderAndDateOfBirthToUsers', N'9.0.20');

DROP INDEX [IX_PasswordResetRequests_UserId_Status] ON [PasswordResetRequests];

DECLARE @var1 sysname;
SELECT @var1 = [d].[name]
FROM [sys].[default_constraints] [d]
INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
WHERE ([d].[parent_object_id] = OBJECT_ID(N'[Users]') AND [c].[name] = N'Phone');
IF @var1 IS NOT NULL EXEC(N'ALTER TABLE [Users] DROP CONSTRAINT [' + @var1 + '];');
ALTER TABLE [Users] ALTER COLUMN [Phone] nvarchar(max) NULL;

DECLARE @var2 sysname;
SELECT @var2 = [d].[name]
FROM [sys].[default_constraints] [d]
INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
WHERE ([d].[parent_object_id] = OBJECT_ID(N'[Users]') AND [c].[name] = N'Gender');
IF @var2 IS NOT NULL EXEC(N'ALTER TABLE [Users] DROP CONSTRAINT [' + @var2 + '];');
ALTER TABLE [Users] ALTER COLUMN [Gender] nvarchar(max) NULL;

DECLARE @var3 sysname;
SELECT @var3 = [d].[name]
FROM [sys].[default_constraints] [d]
INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
WHERE ([d].[parent_object_id] = OBJECT_ID(N'[PasswordResetRequests]') AND [c].[name] = N'Status');
IF @var3 IS NOT NULL EXEC(N'ALTER TABLE [PasswordResetRequests] DROP CONSTRAINT [' + @var3 + '];');
ALTER TABLE [PasswordResetRequests] ALTER COLUMN [Status] nvarchar(max) NOT NULL;

DECLARE @var4 sysname;
SELECT @var4 = [d].[name]
FROM [sys].[default_constraints] [d]
INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
WHERE ([d].[parent_object_id] = OBJECT_ID(N'[PasswordResetRequests]') AND [c].[name] = N'EncryptedPassword');
IF @var4 IS NOT NULL EXEC(N'ALTER TABLE [PasswordResetRequests] DROP CONSTRAINT [' + @var4 + '];');
ALTER TABLE [PasswordResetRequests] ALTER COLUMN [EncryptedPassword] nvarchar(max) NOT NULL;

CREATE INDEX [IX_PasswordResetRequests_UserId] ON [PasswordResetRequests] ([UserId]);

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20261112035538_AddUserProfileColumns', N'9.0.20');

ALTER TABLE [Products] ADD [IsVisible] bit NOT NULL DEFAULT CAST(1 AS bit);

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20261112105213_AddProductVisibility', N'9.0.20');

IF OBJECT_ID(N'dbo.Employees', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[Employees] (
        [Id] INT IDENTITY(1,1) NOT NULL,
        [EmployeeCode] NVARCHAR(20) NOT NULL,
        [FullName] NVARCHAR(150) NOT NULL,
        [Email] NVARCHAR(255) NOT NULL,
        [Phone] NVARCHAR(20) NOT NULL,
        [Position] NVARCHAR(100) NOT NULL,
        [Status] NVARCHAR(20) NOT NULL,
        [UserId] INT NULL,
        [CanManageProducts] BIT NOT NULL,
        [CanManageOrders] BIT NOT NULL,
        [CanManageInventory] BIT NOT NULL,
        [CreatedAt] DATETIME2(7) NOT NULL
            CONSTRAINT [DF_Employees_CreatedAt]
            DEFAULT (SYSUTCDATETIME()),
        CONSTRAINT [PK_Employees] PRIMARY KEY ([Id]),
        CONSTRAINT [UQ_Employees_EmployeeCode]
            UNIQUE ([EmployeeCode]),
        CONSTRAINT [UQ_Employees_Email] UNIQUE ([Email]),
        CONSTRAINT [FK_Employees_Users_UserId]
            FOREIGN KEY ([UserId]) REFERENCES [dbo].[Users] ([Id])
    );
END;

IF OBJECT_ID(N'dbo.EmployeeActivityLogs', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[EmployeeActivityLogs] (
        [Id] BIGINT IDENTITY(1,1) NOT NULL,
        [EmployeeId] INT NULL,
        [ActorUserId] INT NULL,
        [Action] NVARCHAR(100) NOT NULL,
        [Details] NVARCHAR(1000) NULL,
        [CreatedAt] DATETIME2(7) NOT NULL
            CONSTRAINT [DF_EmployeeActivityLogs_CreatedAt]
            DEFAULT (SYSUTCDATETIME()),
        CONSTRAINT [PK_EmployeeActivityLogs] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_EmployeeActivityLogs_Employees_EmployeeId]
            FOREIGN KEY ([EmployeeId]) REFERENCES [dbo].[Employees] ([Id]),
        CONSTRAINT [FK_EmployeeActivityLogs_Users_ActorUserId]
            FOREIGN KEY ([ActorUserId]) REFERENCES [dbo].[Users] ([Id])
    );
END;

IF EXISTS (
    SELECT 1
    FROM sys.key_constraints
    WHERE [name] = N'UQ_Employees_UserId'
      AND [parent_object_id] = OBJECT_ID(N'dbo.Employees'))
BEGIN
    ALTER TABLE [dbo].[Employees]
        DROP CONSTRAINT [UQ_Employees_UserId];
END;

IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE [object_id] = OBJECT_ID(N'dbo.Employees')
      AND [name] = N'IX_Employees_UserId')
BEGIN
    CREATE UNIQUE INDEX [IX_Employees_UserId]
        ON [dbo].[Employees] ([UserId])
        WHERE [UserId] IS NOT NULL;
END;

IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE [object_id] = OBJECT_ID(N'dbo.EmployeeActivityLogs')
      AND [name] = N'IX_EmployeeActivityLogs_ActorUserId')
    CREATE INDEX [IX_EmployeeActivityLogs_ActorUserId]
        ON [dbo].[EmployeeActivityLogs] ([ActorUserId]);

IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE [object_id] = OBJECT_ID(N'dbo.EmployeeActivityLogs')
      AND [name] = N'IX_EmployeeActivityLogs_CreatedAt')
    CREATE INDEX [IX_EmployeeActivityLogs_CreatedAt]
        ON [dbo].[EmployeeActivityLogs] ([CreatedAt]);

IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE [object_id] = OBJECT_ID(N'dbo.EmployeeActivityLogs')
      AND [name] = N'IX_EmployeeActivityLogs_EmployeeId')
    CREATE INDEX [IX_EmployeeActivityLogs_EmployeeId]
        ON [dbo].[EmployeeActivityLogs] ([EmployeeId]);

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20261113042438_AddEmployeeManagementTables', N'9.0.20');

EXEC sp_rename N'[Users].[Password]', N'PasswordHash', 'COLUMN';

ALTER TABLE [Users] ADD [IsActive] bit NOT NULL DEFAULT CAST(1 AS bit);

ALTER TABLE [Users] ADD [MustChangePassword] bit NOT NULL DEFAULT CAST(0 AS bit);

ALTER TABLE [Users] ADD [Username] nvarchar(100) NULL;

UPDATE [dbo].[Users]
SET [Username] = CONCAT(N'user', [Id])
WHERE [Username] IS NULL;

DECLARE @var5 sysname;
SELECT @var5 = [d].[name]
FROM [sys].[default_constraints] [d]
INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
WHERE ([d].[parent_object_id] = OBJECT_ID(N'[Users]') AND [c].[name] = N'Username');
IF @var5 IS NOT NULL EXEC(N'ALTER TABLE [Users] DROP CONSTRAINT [' + @var5 + '];');
ALTER TABLE [Users] ALTER COLUMN [Username] nvarchar(100) NOT NULL;

ALTER TABLE [PasswordResetRequests] ADD [NewPasswordHash] nvarchar(500) NULL;

CREATE UNIQUE INDEX [IX_Users_Username] ON [Users] ([Username]);

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20261113053039_AddSecureEmployeeAccounts', N'9.0.20');

COMMIT;
GO

