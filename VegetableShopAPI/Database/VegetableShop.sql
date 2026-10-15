CREATE DATABASE VegetableShopDb;
GO
USE VegetableShopDb;
GO

-- 1. B?ng Ng??i dùng
CREATE TABLE Users (
    Id INT IDENTITY(1,1) PRIMARY KEY,
    FullName NVARCHAR(100) NOT NULL,
    Email VARCHAR(100) UNIQUE NOT NULL,
    Password VARCHAR(255) NOT NULL,
    Role VARCHAR(20) DEFAULT 'Customer'
);

-- 2. B?ng Danh m?c s?n ph?m
CREATE TABLE Categories (
    Id INT IDENTITY(1,1) PRIMARY KEY,
    Name NVARCHAR(100) NOT NULL,
    Description NVARCHAR(255)
);

-- 3. B?ng S?n ph?m
CREATE TABLE Products (
    Id INT IDENTITY(1,1) PRIMARY KEY,
    Name NVARCHAR(150) NOT NULL,
    Price DECIMAL(18,2) NOT NULL,
    Quantity INT NOT NULL,
    Unit NVARCHAR(50) NOT NULL, -- ??n v? tính: kg, bó, qu?, v?...
    Image VARCHAR(500),
    Description NVARCHAR(MAX),
    CategoryId INT FOREIGN KEY REFERENCES Categories(Id)
);

-- 4. B?ng ??n hàng
CREATE TABLE Orders (
    Id INT IDENTITY(1,1) PRIMARY KEY,
    UserId INT FOREIGN KEY REFERENCES Users(Id),
    OrderDate DATETIME DEFAULT GETDATE(),
    TotalPrice DECIMAL(18,2) NOT NULL,
    Status NVARCHAR(50) DEFAULT 'Pending',
    ShippingAddress NVARCHAR(255) NOT NULL
);

-- 5. B?ng Chi ti?t ??n hàng
CREATE TABLE OrderDetails (
    Id INT IDENTITY(1,1) PRIMARY KEY,
    OrderId INT FOREIGN KEY REFERENCES Orders(Id),
    ProductId INT FOREIGN KEY REFERENCES Products(Id),
    Quantity INT NOT NULL,
    Price DECIMAL(18,2) NOT NULL
);

USE VegetableShopDb;
GO

-- 1. Thêm Danh m?c
INSERT INTO Categories (Name, Description) VALUES 
(N'Rau xanh', N'Các lo?i rau ?n lá t??i s?ch m?i ngày'),
(N'C?', N'Các lo?i c? qu? giàu dinh d??ng'),
(N'Trái cây', N'Trái cây t??i ngon theo mùa'),
(N'N?m', N'Các lo?i n?m t??i s?ch an toàn');

-- 2. Thêm S?n ph?m m?u
INSERT INTO Products (Name, Price, Quantity, Unit, Image, Description, CategoryId) VALUES 
(N'C?i bó xôi', 15000, 1, N'kg', 'https://images.unsplash.com/photo-1576045057995-568f588f82fb?auto=format&fit=crop&w=500&q=80', N'C?i bó xôi t??i s?ch ??t chu?n VietGAP.', 1),
(N'Rau mu?ng', 10000, 1, N'bó', 'https://images.unsplash.com/photo-1618160702438-9b02ab6515c9?auto=format&fit=crop&w=500&q=80', N'Rau mu?ng xanh t??i, an toàn.', 1),
(N'Cà r?t', 22000, 1, N'kg', 'https://images.unsplash.com/photo-1598170845058-32b9d6a5da37?auto=format&fit=crop&w=500&q=80', N'Cà r?t ?à L?t t??i giòn, ng?t t? nhiên.', 2),
(N'Khoai tây', 25000, 1, N'kg', 'https://images.unsplash.com/photo-1518977676601-b5ff321036b3?auto=format&fit=crop&w=500&q=80', N'Khoai tây s?ch c? l?n, nhi?u tinh b?t t?t.', 2),
(N'Táo ??', 45000, 1, N'kg', 'https://images.unsplash.com/photo-1560806887-1e4cd0b6cbd6?auto=format&fit=crop&w=500&q=80', N'Táo nh?p kh?u giòn ng?t, giàu vitamin.', 3),
(N'N?m kim châm', 18000, 1, N'v?', 'https://images.unsplash.com/photo-1541832676-9b763b0239ab?auto=format&fit=crop&w=500&q=80', N'N?m kim châm t??i ngon cho món l?u.', 4);


IF COL_LENGTH('dbo.Products', 'IsActive') IS NULL
    ALTER TABLE dbo.Products
    ADD IsActive BIT NOT NULL DEFAULT 1;

IF COL_LENGTH('dbo.Orders', 'RecipientName') IS NULL
    ALTER TABLE dbo.Orders
    ADD RecipientName NVARCHAR(100) NOT NULL DEFAULT N'';

IF COL_LENGTH('dbo.Orders', 'Phone') IS NULL
    ALTER TABLE dbo.Orders
    ADD Phone NVARCHAR(20) NOT NULL DEFAULT N'';

IF COL_LENGTH('dbo.Orders', 'Note') IS NULL
    ALTER TABLE dbo.Orders
    ADD Note NVARCHAR(500) NOT NULL DEFAULT N'';

IF COL_LENGTH('dbo.Orders', 'PaymentMethod') IS NULL
    ALTER TABLE dbo.Orders
    ADD PaymentMethod NVARCHAR(20) NOT NULL DEFAULT N'';

IF COL_LENGTH('dbo.Orders', 'ShippingFee') IS NULL
    ALTER TABLE dbo.Orders
    ADD ShippingFee DECIMAL(18,2) NULL;

IF COL_LENGTH('dbo.Orders', 'StockDeducted') IS NULL
    ALTER TABLE dbo.Orders
    ADD StockDeducted BIT NOT NULL DEFAULT 0;

IF COL_LENGTH('dbo.OrderDetails', 'ProductName') IS NULL
    ALTER TABLE dbo.OrderDetails
    ADD ProductName NVARCHAR(150) NOT NULL DEFAULT N'';

IF COL_LENGTH('dbo.OrderDetails', 'Unit') IS NULL
    ALTER TABLE dbo.OrderDetails
    ADD Unit NVARCHAR(50) NOT NULL DEFAULT N'';


