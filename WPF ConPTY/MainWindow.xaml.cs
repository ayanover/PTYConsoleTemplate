using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Documents;
using GUIConsole.ConPTY;
using WPF_ConPTY;

namespace TerminalPoC
{
    public partial class MainWindow : Window
    {
        private Terminal _terminal;
        private CancellationTokenSource _readCancellation;
        private bool _autoScroll = true;
        private VT100Formatter _formatter;

        public MainWindow()
        {
            InitializeComponent();
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            LogMessage("Window loaded, initializing terminal...");

            // Initialize the VT100 formatter
            _formatter = new VT100Formatter(OutputRichTextBox);

            // Create the terminal instance
            _terminal = new Terminal();

            // Subscribe to output ready event
            _terminal.OutputReady += Terminal_OutputReady;

            // Start terminal in background thread (IMPORTANT: Start blocks until process exits)
            Task.Run(() => {
                try
                {
                    LogMessage("Starting PowerShell...");
                    // This will block until the process exits!
                    _terminal.Start("powershell.exe -NoProfile -NoExit", 120, 30);
                    LogMessage("PowerShell process has exited");
                }
                catch (Exception ex)
                {
                    Dispatcher.Invoke(() => {
                        LogMessage($"ERROR: Terminal.Start failed: {ex.Message}");
                    });
                }
            });

            LogMessage("Window initialization complete");
        }

        private void Terminal_OutputReady(object sender, EventArgs e)
        {
            LogMessage("Terminal_OutputReady event received");

            // Create cancellation token for reading operations
            _readCancellation = new CancellationTokenSource();

            // Start stream reading
            StartStreamReading();

            // Send test commands after a short delay
            Dispatcher.Invoke(() => {
                Task.Delay(1000).ContinueWith(_ => {
                    try
                    {
                        LogMessage("Sending test commands...");

                        // Setup PowerShell environment
                        _terminal.WriteToPseudoConsole("$OutputEncoding = [System.Text.Encoding]::UTF8\r\n");
                        _terminal.WriteToPseudoConsole("$host.UI.RawUI.ForegroundColor = 'Green'\r\n");

                        // Send test output commands
                        _terminal.WriteToPseudoConsole("Write-Host 'TEST OUTPUT FROM POWERSHELL' -ForegroundColor Cyan\r\n");
                        _terminal.WriteToPseudoConsole("Get-Process | Select-Object -First 3 | Format-Table\r\n");

                        // Send a looping command to generate continuous output
                        _terminal.WriteToPseudoConsole("for ($i=1; $i -le 5; $i++) { Write-Host \"Line $i\" -ForegroundColor Yellow; Start-Sleep -Milliseconds 500 }\r\n");

                        LogMessage("Test commands sent");
                    }
                    catch (Exception ex)
                    {
                        LogMessage($"ERROR sending command: {ex.Message}");
                    }
                });
            });
        }

        private void StartStreamReading()
        {
            Task.Run(() => {
                LogMessage("Starting Stream reading...");
                byte[] buffer = new byte[4096];

                try
                {
                    // Make sure we have a valid stream
                    if (_terminal.ConsoleOutStream == null)
                    {
                        LogMessage("ERROR: ConsoleOutStream is null");
                        return;
                    }

                    while (!_readCancellation.IsCancellationRequested)
                    {
                        try
                        {
                            // Basic reading from the stream
                            int bytesRead = _terminal.ConsoleOutStream.Read(buffer, 0, buffer.Length);

                            if (bytesRead > 0)
                            {
                                string text = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                                LogMessage($"Stream.Read: {bytesRead} bytes");

                                // Process and display the text with VT100 formatting
                                Dispatcher.Invoke(() => {
                                    _formatter.ProcessText(text);
                                });
                            }
                            else
                            {
                                // No bytes read - small wait before trying again
                                Thread.Sleep(10);
                            }
                        }
                        catch (Exception ex)
                        {
                            LogMessage($"Stream.Read error: {ex.Message}");
                            Thread.Sleep(500);
                        }
                    }
                }
                catch (Exception ex)
                {
                    LogMessage($"Stream reading thread error: {ex.Message}");
                }

                LogMessage("Stream reading thread exited");
            }, _readCancellation.Token);
        }

        private void SendButton_Click(object sender, RoutedEventArgs e)
        {
            SendCommand();
        }

        private void InputTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.None)
            {
                e.Handled = true;
                SendCommand();
            }
        }

        private void SendCommand()
        {
            string command = InputTextBox.Text.Trim();
            if (string.IsNullOrEmpty(command))
                return;

            try
            {
                LogMessage($"Sending command: {command}");

                // Add command to output with a prompt
                _formatter.ProcessText($"\r\n> {command}\r\n");

                // Send to terminal (add newline if needed)
                if (!command.EndsWith("\r\n"))
                    command += "\r\n";

                _terminal.WriteToPseudoConsole(command);

                // Clear input
                InputTextBox.Clear();
            }
            catch (Exception ex)
            {
                LogMessage($"Error sending command: {ex.Message}");
                MessageBox.Show($"Failed to send command: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void LogMessage(string message)
        {
            string timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
            string logMessage = $"[{timestamp}] {message}";

            // Output to debug console
            System.Diagnostics.Debug.WriteLine(logMessage);

            // Add to UI log (with thread safety)
            Dispatcher.InvokeAsync(() => {
                LogTextBox.AppendText(logMessage + Environment.NewLine);
                LogTextBox.ScrollToEnd();
            });
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            LogMessage("Window closing...");

            // Cancel reading operations
            if (_readCancellation != null)
            {
                _readCancellation.Cancel();
            }

            // Try to exit PowerShell gracefully
            try
            {
                _terminal?.WriteToPseudoConsole("exit\r\n");
                LogMessage("Exit command sent to PowerShell");
            }
            catch (Exception ex)
            {
                LogMessage($"Error sending exit command: {ex.Message}");
            }
        }
    }
}