using System.Security.Cryptography;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TDevElectronics.Data;
using TDevElectronics.Helpers;
using TDevElectronics.Models.Entities;
using TDevElectronics.Models.ViewModels;

namespace TDevElectronics.Areas.Admin.Controllers
{
    [Area("Admin")]
    [Authorize(Roles = "Admin")]
    public class UsersController : Controller
    {
        private readonly AppDbContext _context;
        private readonly IConfiguration _config;

        public UsersController(AppDbContext context, IConfiguration config)
        {
            _context = context;
            _config = config;
        }

        // GET: /Admin/Users
        public async Task<IActionResult> Index(string? keyword, string? role, int page = 1)
        {
            const int pageSize = 10;
            var query = _context.Users.AsQueryable();

            if (!string.IsNullOrWhiteSpace(keyword))
            {
                var kw = keyword.Trim().ToLower();
                query = query.Where(u => u.FullName.ToLower().Contains(kw) || u.Email.ToLower().Contains(kw));
            }

            if (!string.IsNullOrWhiteSpace(role))
            {
                query = query.Where(u => u.Role == role);
            }

            var totalUsers = await query.CountAsync();
            var users = await query
                .OrderByDescending(u => u.CreatedAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            ViewBag.Keyword = keyword;
            ViewBag.Role = role;
            ViewBag.CurrentPage = page;
            ViewBag.TotalPages = (int)Math.Ceiling(totalUsers / (double)pageSize);

            // Lấy thêm danh sách mã mời Admin đang hoạt động
            ViewBag.ActiveInvites = await _context.AdminInvites
                .OrderByDescending(i => i.ExpiresAt)
                .Take(10)
                .ToListAsync();

            return View(users);
        }

        // POST: /Admin/Users/ToggleActive/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ToggleActive(int id)
        {
            var user = await _context.Users.FindAsync(id);
            if (user == null)
            {
                return NotFound();
            }

            // Tránh việc admin tự khóa tài khoản của chính mình
            var currentUserId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            if (currentUserId == id.ToString())
            {
                TempData["Error"] = "Bạn không thể khóa tài khoản đang đăng nhập của chính mình.";
                return RedirectToAction(nameof(Index));
            }

            user.IsActive = !user.IsActive;
            await _context.SaveChangesAsync();

            TempData["Success"] = $"Đã {(user.IsActive ? "kích hoạt" : "vô hiệu hóa")} tài khoản {user.Email}.";
            return RedirectToAction(nameof(Index));
        }

        // POST: /Admin/Users/GenerateInvite
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> GenerateInvite(string? targetEmail)
        {
            // Sinh mã mời ngẫu nhiên dạng: TDEV-ADM-XXXX-XXXX
            string rawCode = "TDEV-ADM-" + Convert.ToHexString(RandomNumberGenerator.GetBytes(4)) + "-" + Convert.ToHexString(RandomNumberGenerator.GetBytes(4));
            string codeHash = PasswordHelper.ComputeSha256(rawCode.ToUpperInvariant());
            string lastFour = rawCode.Substring(rawCode.Length - 4);

            int expiryHours = _config.GetValue<int>("AdminInviteCode:DefaultExpiryHours", 48);

            var invite = new AdminInvite
            {
                CodeHash = codeHash,
                LastFourChars = lastFour,
                TargetEmail = string.IsNullOrWhiteSpace(targetEmail) ? null : targetEmail.Trim().ToLower(),
                ExpiresAt = DateTime.UtcNow.AddHours(expiryHours),
                IsRevoked = false
            };

            await _context.AdminInvites.AddAsync(invite);
            await _context.SaveChangesAsync();

            // Lưu mã chưa băm vào TempData để hiển thị 1 lần duy nhất cho Admin sao chép
            TempData["GeneratedInviteCode"] = rawCode;
            TempData["Success"] = "Đã tạo mã mời quản trị mới thành công!";
            return RedirectToAction(nameof(Index));
        }

        // POST: /Admin/Users/RevokeInvite/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RevokeInvite(int id)
        {
            var invite = await _context.AdminInvites.FindAsync(id);
            if (invite != null && invite.UsedAt == null)
            {
                invite.IsRevoked = true;
                await _context.SaveChangesAsync();
                TempData["Success"] = "Đã thu hồi mã mời thành công.";
            }

            return RedirectToAction(nameof(Index));
        }
    
        // GET: /Admin/Users/Edit/5
        [HttpGet]
        public async Task<IActionResult> Edit(int id)
        {
            var u = await _context.Users.FindAsync(id);
            if (u == null) return NotFound();
            return View(new UserEditViewModel { Id = u.Id, Email = u.Email, FullName = u.FullName, Role = u.Role, IsActive = u.IsActive });
        }

        // POST: /Admin/Users/Edit/5 : đổi tên, vai trò, trạng thái. Không tự hạ quyền / khóa mình, luôn còn >= 1 admin hoạt động
        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, UserEditViewModel m)
        {
            var u = await _context.Users.FindAsync(id);
            if (u == null) return NotFound();
            m.Id = id; m.Email = u.Email;
            if (!ModelState.IsValid) return View(m);

            var isSelf = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value == id.ToString();
            if (isSelf && (m.Role != "Admin" || !m.IsActive))
            {
                ModelState.AddModelError("", "Bạn không thể hạ quyền hoặc khóa chính tài khoản đang đăng nhập.");
                return View(m);
            }
            var losesAdmin = u.Role == "Admin" && u.IsActive && (m.Role != "Admin" || !m.IsActive);
            if (losesAdmin && !await _context.Users.AnyAsync(x => x.Id != id && x.Role == "Admin" && x.IsActive))
            {
                ModelState.AddModelError("", "Hệ thống phải còn ít nhất một quản trị viên hoạt động.");
                return View(m);
            }

            u.FullName = m.FullName.Trim();
            u.Role = m.Role;
            u.IsActive = m.IsActive;
            u.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();
            TempData["Success"] = $"Đã cập nhật tài khoản {u.Email}.";
            return RedirectToAction(nameof(Index));
        }

        // POST: /Admin/Users/Delete/5 : không xóa chính mình, admin cuối cùng, hoặc tài khoản đã có lời mời (hãy đặt Inactive)
        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(int id)
        {
            var u = await _context.Users.FindAsync(id);
            if (u == null) return NotFound();

            if (User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value == id.ToString())
                TempData["Error"] = "Bạn không thể xóa tài khoản đang đăng nhập.";
            else if (u.Role == "Admin" && !await _context.Users.AnyAsync(x => x.Id != id && x.Role == "Admin"))
                TempData["Error"] = "Phải còn ít nhất một quản trị viên.";
            else if (await _context.AdminInvites.AnyAsync(i => i.CreatedByAdminId == id || i.UsedByUserId == id))
                TempData["Error"] = "Tài khoản này đã liên quan tới mã mời nên không xóa được. Hãy đặt Inactive (Khóa).";
            else
            {
                _context.Users.Remove(u);
                await _context.SaveChangesAsync();
                TempData["Success"] = $"Đã xóa tài khoản {u.Email}.";
            }
            return RedirectToAction(nameof(Index));
        }

        // GET: /Admin/Users/Invites : toàn bộ mã mời kèm trạng thái Active, Used, Expired, Revoked
        [HttpGet]
        public async Task<IActionResult> Invites()
        {
            var list = await _context.AdminInvites.OrderByDescending(i => i.CreatedAt).Take(100).ToListAsync();
            return View(list);
        }
    }
}