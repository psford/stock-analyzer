BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260123182425_AddSecurityMasterAndPrices'
)
BEGIN
    IF SCHEMA_ID(N'data') IS NULL EXEC(N'CREATE SCHEMA [data];');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260123182425_AddSecurityMasterAndPrices'
)
BEGIN
    CREATE TABLE [data].[SecurityMaster] (
        [SecurityAlias] int NOT NULL IDENTITY,
        [PrimaryAssetId] nvarchar(50) NULL,
        [IssueName] nvarchar(200) NOT NULL,
        [TickerSymbol] nvarchar(20) NOT NULL,
        [Exchange] nvarchar(50) NULL,
        [SecurityType] nvarchar(50) NULL,
        [IsActive] bit NOT NULL DEFAULT CAST(1 AS bit),
        [CreatedAt] datetime2 NOT NULL DEFAULT (GETUTCDATE()),
        [UpdatedAt] datetime2 NOT NULL DEFAULT (GETUTCDATE()),
        CONSTRAINT [PK_SecurityMaster] PRIMARY KEY ([SecurityAlias])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260123182425_AddSecurityMasterAndPrices'
)
BEGIN
    CREATE TABLE [data].[Prices] (
        [Id] bigint NOT NULL IDENTITY,
        [SecurityAlias] int NOT NULL,
        [EffectiveDate] date NOT NULL,
        [Open] decimal(18,4) NOT NULL,
        [High] decimal(18,4) NOT NULL,
        [Low] decimal(18,4) NOT NULL,
        [Close] decimal(18,4) NOT NULL,
        [Volatility] decimal(10,6) NULL,
        [Volume] bigint NULL,
        [AdjustedClose] decimal(18,4) NULL,
        [CreatedAt] datetime2 NOT NULL DEFAULT (GETUTCDATE()),
        CONSTRAINT [PK_Prices] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Prices_SecurityMaster_SecurityAlias] FOREIGN KEY ([SecurityAlias]) REFERENCES [data].[SecurityMaster] ([SecurityAlias]) ON DELETE CASCADE
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260123182425_AddSecurityMasterAndPrices'
)
BEGIN
    CREATE INDEX [IX_Prices_EffectiveDate] ON [data].[Prices] ([EffectiveDate]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260123182425_AddSecurityMasterAndPrices'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Prices_SecurityAlias_EffectiveDate] ON [data].[Prices] ([SecurityAlias], [EffectiveDate]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260123182425_AddSecurityMasterAndPrices'
)
BEGIN
    CREATE INDEX [IX_SecurityMaster_IsActive] ON [data].[SecurityMaster] ([IsActive]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260123182425_AddSecurityMasterAndPrices'
)
BEGIN
    CREATE UNIQUE INDEX [IX_SecurityMaster_TickerSymbol] ON [data].[SecurityMaster] ([TickerSymbol]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260123182425_AddSecurityMasterAndPrices'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260123182425_AddSecurityMasterAndPrices', N'8.0.23');
END;
GO

COMMIT;
GO
