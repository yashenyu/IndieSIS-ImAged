using System;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using Newtonsoft.Json; // Install via NuGet: Newtonsoft.Json

namespace ImAged.MVVM.View
{
    public partial class Login : Window
    {
        private static readonly HttpClient client = new HttpClient();

        public Login()
        {
            InitializeComponent();
        }

        private async void Login_Click(object sender, RoutedEventArgs e)
        {
            string username = UsernameTextBox.Text.Trim();
            string password = PasswordBox.Password.Trim();

            if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
            {
                MessageBox.Show("Please enter your credentials.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            try
            {
                var loginData = new { username, password };
                var json = JsonConvert.SerializeObject(loginData);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                // Change this URL to your actual backend endpoint
                var response = await client.PostAsync("http://localhost:8080/api/login", content);

                if (response.IsSuccessStatusCode)
                {
                    MessageBox.Show("Login successful!", "Welcome", MessageBoxButton.OK, MessageBoxImage.Information);

                    // Open main app window
                    MainWindow main = new MainWindow();
                    main.Show();
                    this.Close();
                }
                else
                {
                    MessageBox.Show("Invalid username or password.", "Access Denied", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"An error occurred: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
