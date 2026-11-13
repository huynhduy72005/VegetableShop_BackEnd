using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VegetableShopAPI.Migrations
{
    /// <inheritdoc />
    public partial class AddEmployeeManagementTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
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
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EmployeeActivityLogs");

            migrationBuilder.DropTable(
                name: "Employees");
        }
    }
}
