﻿using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Shop_Mvc.Models;
using Shop_Mvc.Services;
using System.Net.Mail;
using MailKit.Net.Smtp;
using MimeKit;
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;
using Newtonsoft.Json;
using Microsoft.IdentityModel.Tokens;
using System.Reflection.Metadata;

namespace Shop_Mvc.Controllers
{
    public class UserController : Controller
    {
        private readonly ILogger<HomeController> _logger;
        private readonly IDatabaseServise _DatabaseServise;
        private readonly IMemoryCache _memoryCache;
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly UserManager<User> _userManager;
        private readonly SignInManager<User> _signInManager;

        public UserController(ILogger<HomeController> logger, IDatabaseServise DatabaseServise, IMemoryCache memoryCache, IHttpContextAccessor httpContextAccessor, UserManager<User> userManager, SignInManager<User> signInManager)
        {
            _logger = logger;
            _DatabaseServise = DatabaseServise;
            _memoryCache = memoryCache;
            _httpContextAccessor = httpContextAccessor;
            _userManager = userManager;
            _signInManager = signInManager;
        }

        [HttpPost]
        public async Task<IActionResult> Register(string email, string password, string name)
        {
            if (ModelState.IsValid)
            {
                var user = new User { UserName = email, Email = email, Name = name };
                var result = await _userManager.CreateAsync(user, password);
                user = _userManager.FindByEmailAsync(email).Result;
                var token = await _userManager.GenerateEmailConfirmationTokenAsync(user);

                var confirmationLink = Url.Action("ConfirmEmail", "User", new
                {
                    userId = user.Id,
                    token = token
                }, protocol: HttpContext.Request.Scheme);

                if (result.Succeeded)
                {
                    await _signInManager.SignInAsync(user, isPersistent: false);

                    var message = new MimeMessage();
                    message.From.Add(new MailboxAddress("NOVUS", "noreplynovus547@gmail.com"));
                    message.To.Add(new MailboxAddress(name, email));
                    message.Subject = "NOVUS";
                    var htmlText = $"Дякуємо за реєстрацію, будь ласка підтвердіть ваш акаунт, перейшовши за посиланням: <a href=\"{confirmationLink}\" target=\"_blank\" rel=\"noreferrer noopener\">Підтвердити акаунт</a>.";
                    var bodyBuilder = new BodyBuilder();
                    bodyBuilder.HtmlBody = htmlText;

                    message.Body = bodyBuilder.ToMessageBody();

                    try
                    {
                        using (var client = new MailKit.Net.Smtp.SmtpClient())
                        {
                            client.Connect("smtp.gmail.com", 587, false);
                            client.Authenticate("noreplynovus547@gmail.com", "ytufrcpfvirgxryb");
                            client.Send(message);
                            client.Disconnect(true);

                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine(ex.ToString());
                    }

                    return Json(true);
                }

                foreach (var error in result.Errors)
                {
                    ModelState.AddModelError(string.Empty, error.Description);
                }
            }

            return Json(false);
        }

        [HttpGet]
        public async Task<IActionResult> Login(string email, string password)
        {
            if (ModelState.IsValid)
            {
                var result = await _signInManager.PasswordSignInAsync(email, password, true, lockoutOnFailure: false);

                if (result.Succeeded)
                {
                    var user = _userManager.Users.FirstOrDefault(u => u.Email == email);
                    var userString = JsonConvert.SerializeObject(user);
                    Response.Cookies.Append("UserCookie", userString, new CookieOptions
                    {
                        HttpOnly = false,
                        SameSite = SameSiteMode.None,
                        Secure = Request.IsHttps,
                        Expires = DateTime.UtcNow.AddMinutes(100000000)
                    });
                    var cartProductsString = _httpContextAccessor.HttpContext.Request.Cookies["MyCookie"];
                    if (cartProductsString != null && cartProductsString.Length > 2)
                    {
                        List<CartProduct> products = JsonConvert.DeserializeObject<List<CartProduct>>(cartProductsString);
                        foreach (var product in products)
                            product.user_Id = user.Id;
                        _DatabaseServise.DeleteUserCartProducts(user.Id);
                        await _DatabaseServise.SetCartProductsAsync(products);
                    }
                    return Json(new { success = true, user });
                }

                ModelState.AddModelError(string.Empty, "Invalid login attempt.");
                return Json(new { success = false });
            }

            return Json(new { success = false, errorMessage = "Invalid ModelState" });
        }


        [HttpGet]
        public IActionResult IsEmailAvailable(string email) => _DatabaseServise.GetUserByEmail(email) == null ? Json(true) : Json(false);

        [HttpGet]
        public IActionResult IsEmailValid(string email)
        {
            try
            {
                MailAddress mailAddress = new MailAddress(email);
                return Json(true);
            }
            catch (FormatException)
            {
                return Json(false);
            }
        }
        private List<CartProduct> GetProductsFromCookie()
        {
            var cookieValue = _httpContextAccessor.HttpContext.Request.Cookies["MyCookie"];
            var userString = _httpContextAccessor.HttpContext.Request.Cookies["UserCookie"];
            if (!userString.IsNullOrEmpty())
            {
                var user = JsonConvert.DeserializeObject<User>(userString);
                List<CartProduct> cartProducts = _DatabaseServise.GetCartProductsAsync(user.Id).GetAwaiter().GetResult();
                return cartProducts;
            }
            else
            {
                if (!string.IsNullOrEmpty(cookieValue))
                {
                    var products = JsonConvert.DeserializeObject<List<CartProduct>>(cookieValue);

                    return products;
                }
            }
            return new List<CartProduct>();
        }

        [HttpGet]
        public async Task<IActionResult> ConfirmEmail(string userId, string token)
        {
            if (userId == null || token == null)
            {
                return RedirectToAction("Error", "Home");
            }

            var user = await _userManager.FindByIdAsync(userId);
            if (user == null)
            {
                return RedirectToAction("Error", "Home");
            }

            var result = await _userManager.ConfirmEmailAsync(user, token);
            if (result.Succeeded)
            {
                return RedirectToAction("Index", "Home");

            }
            else
            {
                return RedirectToAction("Error", "Home");
            }
        }

        private IEnumerable<UserFavoriteProduct> SetFieldIsInCart(IEnumerable<UserFavoriteProduct> products, List<CartProduct> cartProducts)
        {
            var commonIds = cartProducts.Select(p => p.product_Id).ToList();

            for (var i = 0; i < commonIds.Count; i++)
            {
                var id = commonIds[i];
                foreach (var product in products.Where(p => p.product_Id == id))
                {
                    product.inCart = cartProducts[i];
                }
            }
            return products;
        }

        [HttpGet]
        public IActionResult UserProfileView(string contentName) => View("UserProfileView", contentName);

        [HttpGet]
        public IActionResult UserProfileSwitcherPartialView()
        {
            var userString = Request.Cookies["UserCookie"];
            if (userString != null)
            {
                var user = JsonConvert.DeserializeObject<User>(userString);
                return PartialView(user);
            }

            return PartialView();
        }

        public IActionResult IsUserExist(string email, string password)
        {
            var user = _userManager.FindByEmailAsync(email).Result;

            if (user != null)
            {
                var signInResult = _signInManager.CheckPasswordSignInAsync(user, password, lockoutOnFailure: false).Result;

                if (signInResult.Succeeded)
                {
                    return Json(signInResult);
                }
            }
            return Json(false);
        }

        public IActionResult IsEmailConfirmed(string email)
        {
            var user = _userManager?.FindByEmailAsync(email).Result;
            if (user != null)
            {
                return user.EmailConfirmed ? Json(true) : Json(false);
            }
            else return Json(false);
        }

        [HttpGet]
        public IActionResult LogOut()
        {
            Response.Cookies.Delete("UserCookie");
            Response.Cookies.Delete("MyCookie");
            return RedirectToAction("Index", "Home");
        }

        public IActionResult IsUserAuthorized()
        {
            var userString = Request.Cookies["UserCookie"];
            return userString == null ? Json(false) : Json(true);
        }

        public IActionResult GetUserCartProduct(int productId, string userId)
        {
            return Json(_DatabaseServise.GetCartProduct(productId, userId));
        }

        public void UpdateUserCartProduct(int productId, string userId, int count)
        {
            _DatabaseServise.UpdateCartProduct(productId, userId, count);
        }

        public IActionResult GetUserCartProducts(string userId)
        {
            return Json(_DatabaseServise.GetCartProductsAsync(userId).Result);
        }

        public void DeleteUserCartProduct(string userId, int productId) => _DatabaseServise.DeleteUserCartProduct(productId, userId);
        public void AddUserCartProduct(int productId, string userId, decimal price, string title, int count, string promo, string brand, string country) => _DatabaseServise.AddUserCartProduct(productId, userId, price, title, count, promo, brand, country);
        public IActionResult GetUserCartProductCount(int productId, string userId) => Json(_DatabaseServise.GetUserCartProductCount(productId, userId));
        public void RemoveFavoriteProduct(int productId, string userId)
        {
            _DatabaseServise.RemoveProductFromFavorite(productId, userId);
        }
        public void AddFavoriteProduct(int productId, string userId, string title, decimal price, string country, string brand, string promo)
        {
            _DatabaseServise.AddProductToFavorite(productId, userId, title, price, country, brand, promo);
        }

        public IActionResult GetUserFavoriteProducts(string userId) => Json(_DatabaseServise.GetUserFavoriteProducts(userId));

        public IActionResult UserOrderedProductsPartialView() => PartialView();

        public IActionResult UserPersonalShelfPartialView(string userId)
        {
            var model = _DatabaseServise.GetUserCarts(userId);
            return PartialView("UserPersonalShelfPartialView", model);
        }

        public IActionResult UserFavoriteProductsPartialView(string userId)
        {
            var model = _DatabaseServise.GetUserFavoriteProducts(userId);
            var productsInCart = GetProductsFromCookie();
            model = SetFieldIsInCart(model, productsInCart);

            return PartialView(model);
        }

        public void DeleteUserFavoriteProducts(string idToRemove, string userId)
        {
            List<int> idsToRemove = idToRemove.Split(',').Select(int.Parse).ToList();
            _DatabaseServise.DeleteUserFavoriteProducts(idsToRemove, userId);
        }
        public IActionResult UserCartPartialView(int cartId)
        {
            var cart = _DatabaseServise.GetCartById(cartId);
            return PartialView(cart);
        }
        public void DeleteUserCartProducts(string userId) => _DatabaseServise.DeleteUserCartProducts(userId);

        public void CreateUserCart(string user_Id, string cartName) => _DatabaseServise.CreateUserCart(user_Id, cartName);
        public void DeleteUserCart(int cartId, string userId) => _DatabaseServise.DeleteUserCart(cartId, userId);
        public void UpdateProfileUserCartProduct(int cartId, int productId, int count) => _DatabaseServise.UpdateUserCartProduct(cartId, productId, count);
        public void DeleteProfileUserCartProduct(int cartId, int productId) => _DatabaseServise.DeleteProfileUserCartProduct(cartId, productId);
        public void RenameUserCart(int cartId, string cartName) => _DatabaseServise.RenameUserCart(cartId, cartName);
        public IActionResult UserSettingsPartialView(string userId)
        {
            var user = _DatabaseServise.GetUserById(userId);
            return PartialView(user);
        }
        public void UpdateUserPersonalData(string userId, string name, string surname, string secondName, string phoneNumber) => _DatabaseServise.UpdateUserPersonalData(userId, name, surname, secondName, phoneNumber);
    }
}
