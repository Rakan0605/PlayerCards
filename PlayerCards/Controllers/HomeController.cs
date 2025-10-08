using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PlayerCards.Entities;
using PlayerCards.Data;
using PlayerCards.Models;
using System.Security.Claims;

namespace PlayerCards.Controllers
{
    [Authorize] // require login for all Home endpoints
    public class HomeController : Controller
    {
        private readonly AppDbContext _context;
        private readonly IWebHostEnvironment _environment;

        public HomeController(AppDbContext context, IWebHostEnvironment environment)
        {
            _context = context;
            _environment = environment;
        }

        // Helper to get current logged-in user Id (int)
        private int GetCurrentUserId()
        {
            var idStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
            return int.TryParse(idStr, out var id) ? id : 0;
        }

        // user's own cards
        public async Task<IActionResult> Index()
        {
            var userId = GetCurrentUserId();
            if (userId == 0) return RedirectToAction("Login", "Account");

            var cards = await _context.PlayerCards
                .Where(c => c.UserAccountId == userId)
                .ToListAsync();

            return View(cards);
        }

        [HttpGet]
        public IActionResult Create(int? id)
        {
            if (id.HasValue)
            {
                var card = _context.PlayerCards.FirstOrDefault(c => c.Id == id.Value && c.UserAccountId == GetCurrentUserId());
                if (card != null)
                {
                    return View(card);
                }
            }
            return View(new PlayerCard());
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(PlayerCard card, IFormFile imageFile)
        {
            var userId = GetCurrentUserId();
            if (userId == 0) return RedirectToAction("Login", "Account");

            if (imageFile != null && imageFile.Length > 0)
            {
                var uploadsFolder = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "images");
                Directory.CreateDirectory(uploadsFolder);

                //  Get sanitized name if middleware modified it
                var sanitizedName = HttpContext.Items["SanitizedFileName_" + imageFile.Name] as string ?? imageFile.FileName;

                //  Whitelist only image types (extra safety at controller level)
                var allowedExtensions = new[] { ".jpg", ".jpeg", ".png", ".gif", ".webp" };
                var extension = Path.GetExtension(sanitizedName).ToLowerInvariant();

                if (!allowedExtensions.Contains(extension))
                {
                    TempData["Error"] = "Only image files (.jpg, .jpeg, .png, .gif, .webp) are allowed.";
                    return RedirectToAction("Index");
                }

                // Save with unique filename
                var fileName = Guid.NewGuid().ToString() + extension;
                var path = Path.Combine(uploadsFolder, fileName);

                using (var stream = new FileStream(path, FileMode.Create))
                {
                    await imageFile.CopyToAsync(stream);
                }

                card.ImagePath = "/images/" + fileName;
            }

            // create or update
            if (card.Id == 0)
            {
                card.UserAccountId = userId;
                _context.PlayerCards.Add(card);
                await _context.SaveChangesAsync();
            }
            else
            {
                var existing = await _context.PlayerCards
                    .FirstOrDefaultAsync(c => c.Id == card.Id && c.UserAccountId == userId);

                if (existing != null)
                {
                    existing.Name = card.Name;
                    existing.Description = card.Description;
                    existing.Price = card.Price;
                    if (!string.IsNullOrEmpty(card.ImagePath))
                        existing.ImagePath = card.ImagePath;

                    await _context.SaveChangesAsync();
                }
            }

            return RedirectToAction("Index");
        }


        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(int id)
        {
            var userId = GetCurrentUserId();
            var card = await _context.PlayerCards.FirstOrDefaultAsync(c => c.Id == id && c.UserAccountId == userId);
            if (card != null)
            {
                _context.PlayerCards.Remove(card);
                await _context.SaveChangesAsync();
            }
            return RedirectToAction("Index");
        }

        // toggle like/unlike for current user
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Love(int id)
        {
            var userId = GetCurrentUserId();
            if (userId == 0) return RedirectToAction("Login", "Account");

            var existing = await _context.LikedItems
                .FirstOrDefaultAsync(li => li.UserAccountId == userId && li.PlayerCardId == id);

            if (existing != null)
            {
                _context.LikedItems.Remove(existing);
            }
            else
            {
                _context.LikedItems.Add(new LikedItem
                {
                    UserAccountId = userId,
                    PlayerCardId = id
                });
            }

            await _context.SaveChangesAsync();
            return RedirectToAction("Index");
        }



        // add/remove to/from cart
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AddToCart(int id)
        {
            var userId = GetCurrentUserId();
            if (userId == 0) return RedirectToAction("Login", "Account");

            var exists = await _context.CartItems
                .AnyAsync(ci => ci.UserAccountId == userId && ci.PlayerCardId == id);

            if (!exists)
            {
                _context.CartItems.Add(new CartItem
                {
                    UserAccountId = userId,
                    PlayerCardId = id
                });
                await _context.SaveChangesAsync();
            }

            return RedirectToAction("Index");
        }



        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RemoveFromCart(int id)
        {
            var userId = GetCurrentUserId();
            if (userId == 0) return RedirectToAction("Login", "Account");

            var item = await _context.CartItems
                .FirstOrDefaultAsync(ci => ci.UserAccountId == userId && ci.PlayerCardId == id);

            if (item != null)
            {
                _context.CartItems.Remove(item);
                await _context.SaveChangesAsync();
            }

            return RedirectToAction("Index");
        }



        // show user's liked cards
        public async Task<IActionResult> Liked()
        {
            var userId = GetCurrentUserId();
            var likedCards = await _context.LikedItems
                .Where(li => li.UserAccountId == userId)
                .Include(li => li.PlayerCard)
                .Select(li => li.PlayerCard)
                .ToListAsync();

            return View(likedCards);
        }



        // show user's cart
        public async Task<IActionResult> Cart()
        {
            var userId = GetCurrentUserId();
            var cartCards = await _context.CartItems
                .Where(ci => ci.UserAccountId == userId)
                .Include(ci => ci.PlayerCard)
                .Select(ci => ci.PlayerCard)
                .ToListAsync();

            return View(cartCards);
        }



        public IActionResult Privacy() => View();


        // 1. Most Liked PlayerCards
        public async Task<IActionResult> MostLiked()
        {
            var cards = await _context.PlayerCards
                .OrderByDescending(c => c.LikesCount) // assumes you store likes as int
                .Take(10) // top 10
                .ToListAsync();

            return View(cards);
        }

        // 2. Most Added to Cart PlayerCards
        public async Task<IActionResult> MostInCart()
        {
            var cards = await _context.PlayerCards
                .OrderByDescending(c => c.CartCount) // assumes you store cart count
                .Take(10) // top 10
                .ToListAsync();

            return View(cards);
        }
    }

}
