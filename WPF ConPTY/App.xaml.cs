using System;
using System.Globalization;
using System.Threading;
using System.Windows;

namespace GUIConsole.Wpf
{
    public partial class App : Application
    {
        public App()
        {
            // Force the application to use invariant culture to avoid localization resource errors
            Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;
            Thread.CurrentThread.CurrentUICulture = CultureInfo.InvariantCulture;
            
            // Add handler for unhandled exceptions
            DispatcherUnhandledException += App_DispatcherUnhandledException;
        }

        private void App_DispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
        {
            // Log the exception
            Console.WriteLine($"Unhandled exception: {e.Exception}");
            
            // For resource-related errors, try to continue
            if (e.Exception.Message.Contains("resources") || 
                e.Exception.Message.Contains("resource"))
            {
                e.Handled = true;
            }
            else
            {
                MessageBox.Show($"An unexpected error occurred: {e.Exception.Message}", 
                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                e.Handled = true;
            }
        }
    }
}