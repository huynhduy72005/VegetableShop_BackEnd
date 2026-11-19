namespace VegetableShopAPI.Models
{
    public class RevenueSeriesResponseDto
    {
        public string Mode { get; set; } = "day"; // "day" | "month"
        public string FromDate { get; set; } = string.Empty;
        public string ToDate { get; set; } = string.Empty;
        public int Year { get; set; }

        // Tổng hợp toàn kỳ đang lọc
        public decimal TotalGrossRevenue { get; set; }   // Tổng giá trị tất cả đơn đặt
        public decimal TotalNetRevenue { get; set; }     // Doanh thu thực tế (trừ đơn Cancelled)
        public decimal CompletedRevenue { get; set; }    // Doanh thu đơn đã hoàn thành (Completed)
        public int TotalOrders { get; set; }
        public int CompletedOrders { get; set; }
        public int CancelledOrders { get; set; }
        public decimal AverageOrderValue { get; set; }   // Giá trị trung bình / đơn hợp lệ

        public List<RevenuePointDto> Points { get; set; } = new();
    }

    public class RevenuePointDto
    {
        public string Key { get; set; } = string.Empty;      // "2026-11-19" hoặc "2026-11"
        public string Label { get; set; } = string.Empty;    // "19/11" hoặc "Thg 11"
        public string FullDate { get; set; } = string.Empty; // "19/11/2026" hoặc "Tháng 11/2026"
        public decimal Revenue { get; set; }                 // Doanh thu thực (không tính Cancelled)
        public decimal CompletedRevenue { get; set; }        // Doanh thu đơn Completed
        public int TotalOrders { get; set; }
        public int CompletedOrders { get; set; }
        public int CancelledOrders { get; set; }
    }

    public class TopProductReportDto
    {
        public int ProductId { get; set; }
        public string ProductName { get; set; } = string.Empty;
        public string CategoryName { get; set; } = string.Empty;
        public string Unit { get; set; } = string.Empty;
        public string? Image { get; set; }
        public int TotalQuantitySold { get; set; }
        public decimal TotalRevenue { get; set; }
        public int OrderCount { get; set; }
        public int CurrentStock { get; set; }
    }

    public class CategoryRevenueDto
    {
        public int CategoryId { get; set; }
        public string CategoryName { get; set; } = string.Empty;
        public int TotalQuantitySold { get; set; }
        public decimal TotalRevenue { get; set; }
        public double Percentage { get; set; }
    }

    public class AnalyticsRatesResponseDto
    {
        public string FromDate { get; set; } = string.Empty;
        public string ToDate { get; set; } = string.Empty;

        // 1. Thống kê trạng thái & tỷ lệ hủy đơn
        public int TotalOrders { get; set; }
        public int CompletedOrders { get; set; }
        public int PendingOrders { get; set; }
        public int ProcessingOrders { get; set; }
        public int ShippingOrders { get; set; }
        public int CancelledOrders { get; set; }
        public double CancellationRate { get; set; } // % đơn bị hủy
        public double CompletionRate { get; set; }   // % đơn giao thành công

        // 2. Thống kê hao hụt / hư hỏng đặc thù rau củ
        public int TotalSoldUnits { get; set; }          // Tổng sản lượng đã bán
        public int TotalImportedUnits { get; set; }      // Tổng sản lượng nhập lô trong kỳ
        public int TotalDisposedUnits { get; set; }      // Tổng sản lượng xuất hủy (hỏng/hết hạn)
        public double SpoilageRate { get; set; }         // % hao hụt = Hủy / (Bán + Hủy)
        public decimal EstimatedLossValue { get; set; }  // Giá trị thiệt hại vốn ước tính (VNĐ)

        public List<SpoiledProductItemDto> TopSpoiledProducts { get; set; } = new();
        public List<DisposalReasonItemDto> ReasonBreakdown { get; set; } = new();
    }

    public class SpoiledProductItemDto
    {
        public int ProductId { get; set; }
        public string ProductName { get; set; } = string.Empty;
        public string Unit { get; set; } = string.Empty;
        public int DisposedQuantity { get; set; }
        public decimal EstimatedLoss { get; set; }
        public string LastReason { get; set; } = string.Empty;
    }

    public class DisposalReasonItemDto
    {
        public string Reason { get; set; } = string.Empty;
        public int Count { get; set; }
        public int TotalQuantity { get; set; }
    }
}