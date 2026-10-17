USE [VegetableShopDb];
GO

-- 1. THÊM DỮ LIỆU BẢNG CATEGORIES (Danh mục rau củ quả)
INSERT INTO dbo.Categories (Name, Description)
SELECT seed.Name, seed.Description
FROM (VALUES
    (N'Rau xanh', N'Các loại rau ăn lá tươi xanh, thu hoạch trong ngày.'),
    (N'Củ quả', N'Củ quả tươi sạch, giàu dưỡng chất cho bữa ăn gia đình.'),
    (N'Trái cây', N'Trái cây tươi ngon theo mùa, mọng nước và ngọt thanh.'),
    (N'Nấm tươi', N'Các loại nấm tươi dùng cho món xào, canh và lẩu.')
) AS seed(Name, Description)
WHERE NOT EXISTS (
    SELECT 1 FROM dbo.Categories WHERE Name = seed.Name
);


-- 2. THÊM DỮ LIỆU BẢNG PRODUCTS 
INSERT INTO dbo.Products (Name, Price, Quantity, Unit, Image, Description, CategoryId)
SELECT seed.Name, seed.Price, seed.Quantity, seed.Unit, seed.Image, seed.Description, c.Id
FROM (VALUES
    -- Rau xanh
    (N'Rau chân vịt (Cải bina)', CAST(25000 AS DECIMAL(18,2)), 50, N'bó', N'https://images.unsplash.com/photo-1576045057995-568f588f82fb?w=600', N'Rau chân vịt xanh tươi, giàu sắt và vitamin.', N'Rau xanh'),
    (N'Rau muống hữu cơ', CAST(15000 AS DECIMAL(18,2)), 80, N'bó', N'https://images.unsplash.com/photo-1618160702438-9b02ab6515c9?w=600', N'Rau muống ngọn nhỏ, giòn ngọt thích hợp xào tỏi.', N'Rau xanh'),
    (N'Xà lách thủy canh', CAST(35000 AS DECIMAL(18,2)), 40, N'kg', N'https://images.unsplash.com/photo-1622206151226-18ca2c9ab4a1?w=600', N'Xà lách lá mỏng, thích hợp làm món salad tươi mát.', N'Rau xanh'),
    
    -- Củ quả
    (N'Cà rốt Đà Lạt', CAST(22000 AS DECIMAL(18,2)), 60, N'kg', N'https://images.unsplash.com/photo-1598170845058-32b9d6a5da37?w=600', N'Cà rốt giòn ngọt, màu đỏ cam tự nhiên giàu Beta-Carotene.', N'Củ quả'),
    (N'Cà chua bi Đà Lạt', CAST(45000 AS DECIMAL(18,2)), 35, N'kg', N'https://images.unsplash.com/photo-1592924357228-91a4daadcfea?w=600', N'Cà chua bi mọng nước, vị chua ngọt tự nhiên.', N'Củ quả'),
    (N'Khoai lang mật', CAST(38000 AS DECIMAL(18,2)), 45, N'kg', N'https://images.unsplash.com/photo-1596040033229-a9821ebd058d?w=600', N'Khoai lang mật dẻo ngọt, nướng hoặc hấp đều ngon.', N'Củ quả'),
    (N'Dưa leo baby', CAST(30000 AS DECIMAL(18,2)), 50, N'kg', N'https://images.unsplash.com/photo-1449300079323-02e20989d8a2?w=600', N'Dưa leo giòn rụm, vỏ mỏng ăn trực tiếp.', N'Củ quả'),

    -- Trái cây
    (N'Cam sành Vĩnh Long', CAST(35000 AS DECIMAL(18,2)), 70, N'kg', N'https://images.unsplash.com/photo-1611080626919-7cf5a9dbab5b?w=600', N'Cam sành mọng nước, vị ngọt thanh tự nhiên.', N'Trái cây'),
    (N'Dâu tây Đà Lạt', CAST(120000 AS DECIMAL(18,2)), 25, N'hộp', N'https://images.unsplash.com/photo-1464965911861-746a04b4bca6?w=600', N'Dâu tây trái to, quả chín đỏ thơm lừng.', N'Trái cây'),
    (N'Táo Fuji', CAST(65000 AS DECIMAL(18,2)), 40, N'kg', N'https://images.unsplash.com/photo-1560806887-1e4cd0b6cbd6?w=600', N'Táo giòn ngọt, vỏ đỏ bóng đẹp mắt.', N'Trái cây'),

    -- Nấm tươi
    (N'Nấm kim châm', CAST(18000 AS DECIMAL(18,2)), 60, N'gói', N'https://images.unsplash.com/photo-1504545102780-26774c1bb073?w=600', N'Nấm kim châm trắng giòn, thích hợp nấu lẩu.', N'Nấm tươi'),
    (N'Nấm đùi gà', CAST(42000 AS DECIMAL(18,2)), 30, N'kg', N'https://images.unsplash.com/photo-1504545102780-26774c1bb073?w=600', N'Nấm đùi gà thịt dày, dai ngọt tự nhiên.', N'Nấm tươi')
) AS seed(Name, Price, Quantity, Unit, Image, Description, CategoryName)
INNER JOIN dbo.Categories c ON c.Name = seed.CategoryName
WHERE NOT EXISTS (
    SELECT 1 FROM dbo.Products WHERE Name = seed.Name
);


-- 3. THÊM DỮ LIỆU BẢNG ORDERDETAILS (Chi tiết đơn hàng - Khớp động theo Order hiện có)
-- Tự động lấy các đơn hàng đang có trong bảng Orders và ghép sản phẩm vào
INSERT INTO dbo.OrderDetails (OrderId, ProductId, Quantity, Price)
SELECT 
    o.Id AS OrderId,
    p.Id AS ProductId,
    item.Quantity,
    p.Price
FROM dbo.Orders o
CROSS APPLY (
    SELECT TOP 2 Id, Price FROM dbo.Products ORDER BY NEWID()
) p
CROSS APPLY (
    SELECT CAST(FLOOR(RAND(CHECKSUM(NEWID())) * 3 + 1) AS INT) AS Quantity
) item
WHERE NOT EXISTS (
    SELECT 1 FROM dbo.OrderDetails od WHERE od.OrderId = o.Id AND od.ProductId = p.Id
);