using Microsoft.EntityFrameworkCore;
using TDevElectronics.Helpers;
using TDevElectronics.Models.Entities;
// using TDevElectronics.Helpers;   // <- thêm using chứa PasswordHelper (xem bên dưới)

namespace TDevElectronics.Data
{
    public static class DbSeeder
    {
        public static async Task SeedAsync(AppDbContext context)
        {
            // Áp dụng migration (tự tạo database + bảng nếu chưa có)
            await context.Database.MigrateAsync();

            // Chỉ tạo admin mặc định nếu bảng Users chưa có admin nào
            if (!context.Users.Any(u => u.Role == "Admin"))
            {
                var (hash, salt) = PasswordHelper.HashPassword("MẬT_KHẨU_ADMIN_CŨ");

                var defaultAdmin = new User
                {
                    FullName = "Quản trị viên hệ thống",
                    Email = "admin@tdevelectronics.local",
                    PasswordHash = hash,
                    PasswordSalt = salt,
                    Role = "Admin",
                    IsActive = true,
                    CreatedAt = DateTime.UtcNow
                };

                await context.Users.AddAsync(defaultAdmin);
                await context.SaveChangesAsync();
            }
        }
    }
}