using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace VegetableShopAPI.Models
{
    [Table("BatchDisposalLogs")]
    public class BatchDisposalLog
    {
        public int Id { get; set; }

        public int ProductBatchId { get; set; }

        public int ProductId { get; set; }

        public int? ProductVariantId { get; set; }

        public int QuantityDisposed { get; set; }

        [Required]
        [MaxLength(300)]
        public string Reason { get; set; } = string.Empty;

        public DateTime DisposedAt { get; set; } = DateTime.Now;

        public int? DisposedByUserId { get; set; }

        [JsonIgnore]
        public ProductBatch? ProductBatch { get; set; }
    }
}