using System;
using System.Threading.Tasks;
using Microsoft.JSInterop;

namespace frontend.Services
{
    public class ThemeService
    {
        private readonly IJSRuntime _jsRuntime;
        public bool IsDarkMode { get; private set; } = true;
        public event Action? OnThemeChanged;

        public ThemeService(IJSRuntime jsRuntime)
        {
            _jsRuntime = jsRuntime;
        }

        public async Task InitializeAsync()
        {
            try
            {
                var savedTheme = await _jsRuntime.InvokeAsync<string>("localStorage.getItem", "theme");
                if (!string.IsNullOrEmpty(savedTheme))
                {
                    IsDarkMode = savedTheme == "dark";
                }
                else
                {
                    IsDarkMode = true; // Default dark mode for modern look
                }
                await ApplyThemeAsync();
            }
            catch
            {
                IsDarkMode = true;
            }
        }

        public async Task ToggleThemeAsync()
        {
            IsDarkMode = !IsDarkMode;
            try
            {
                await _jsRuntime.InvokeVoidAsync("localStorage.setItem", "theme", IsDarkMode ? "dark" : "light");
                await ApplyThemeAsync();
            }
            catch { }
            OnThemeChanged?.Invoke();
        }

        private async Task ApplyThemeAsync()
        {
            try
            {
                await _jsRuntime.InvokeVoidAsync("document.documentElement.setAttribute", "data-theme", IsDarkMode ? "dark" : "light");
            }
            catch { }
        }
    }
}
