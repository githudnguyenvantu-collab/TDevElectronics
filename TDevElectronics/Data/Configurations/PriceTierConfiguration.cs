using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PriceTierEntity = TDevElectronics.Models.Entities.PriceTier;

namespace TDevElectronics.Data.Configurations
{
    /// <summary>Cấu hình bảng PriceTier. Giai đoạn 1 CHƯA được áp dụng (AppDbContext chưa có DbSet).
    /// Giai đoạn 2: thêm DbSet rồi gọi modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly) và Add-Migration.</summary>
    public class PriceTierConfiguration : IEntityTypeConfiguration<PriceTierEntity>
    {
        public void Configure(EntityTypeBuilder<PriceTierEntity> b)
        {
            b.Property(x => x.UnitPrice).HasPrecision(18, 2);
        }
    }
}