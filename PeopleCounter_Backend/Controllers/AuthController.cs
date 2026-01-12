using Azure.Core;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.Data;
using Microsoft.AspNetCore.Mvc;
using PeopleCounter_Backend.Data;
using PeopleCounter_Backend.Models;
using System.Security.Claims;

namespace PeopleCounter_Backend.Controllers
{
    [ApiController]
    [Route("auth")]
    public class AuthController : ControllerBase
    {
        private readonly IUserRepository _userRepository;

        public AuthController(IUserRepository userRepository)
        {
            _userRepository = userRepository;
        }

        [HttpPost("login")]
        public async Task<IActionResult> Login(Models.LoginRequest request)
        {
            var user = await _userRepository.GetUser(request.Username);
            if (user == null)
            {
                return Unauthorized("Invalid credentials");
            }


            var hasher = new PasswordHasher<object>();
            var result = hasher.VerifyHashedPassword(null, user.PasswordHash, request.Password);

            if (result == PasswordVerificationResult.Failed)
            {
                return Unauthorized("Invalid Password");
            }

            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.NameIdentifier, user.UserId.ToString()),
                new Claim(ClaimTypes.Name, user.Username)
            };
            foreach (var role in user.Roles)
                claims.Add(new Claim(ClaimTypes.Role, role));

            var identity = new ClaimsIdentity(
            claims,
            CookieAuthenticationDefaults.AuthenticationScheme);

            await HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            new ClaimsPrincipal(identity));

            return Ok("Login successful");
        }

        [HttpPost("logout")]
        public async Task<IActionResult> Logout()
        {
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);

            return Ok();
        }


        [HttpPost("create")]
        public async Task<IActionResult> CreateUser(CreateUserRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.Password))
            {
                return BadRequest("Username and password are required");
            }

            if (request.Roles == null || request.Roles.Count == 0)
            {
                return BadRequest("At least one role is required");
            }

            var hasher = new PasswordHasher<object>();
            var passwordHash = hasher.HashPassword(null, request.Password);

            await _userRepository.CreateUser(
                                request.Username,
                               passwordHash,
                               request.Roles);

            return Ok("User created successfully");
        }

        [Authorize]
        [HttpGet("me")]
        public IActionResult Me()
        {
            var user = HttpContext.User;

            var userId = user.FindFirstValue(ClaimTypes.NameIdentifier);
            var username = user.Identity?.Name;

            var roles = user
                .FindAll(ClaimTypes.Role)
                .Select(r => r.Value)
                .ToList();

            return Ok(new
            {
                IsAuthenticated = user.Identity?.IsAuthenticated,
                UserId = userId,
                Username = username,
                Roles = roles
            });
        }
    }
}
