using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

// This aligns with the Microsoft Terminal template
namespace TerminalPoC
{
    public partial class MainWindow : Window
    {
        private GUIConsole.ConPTY.Terminal _terminal;
        private CancellationTokenSource _readCancellation;
        private bool _autoScroll = true;

        // For direct pipe inspection
        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool PeekNamedPipe(
            SafeFileHandle hNamedPipe,
            byte[] lpBuffer,
            uint nBufferSize,
            ref uint lpBytesRead,
            ref uint lpTotalBytesAvail,
            ref uint lpBytesLeftThisMessage);

        // For reading file directly
        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool ReadFile(
            SafeFileHandle hFile,
            byte[] lpBuffer,
            uint nNumberOfBytesToRead,
            ref uint lpNumberOfBytesRead,
            IntPtr lpOverlapped);

        // For flushing output buffer
        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool FlushFileBuffers(SafeFileHandle hFile);

        public MainWindow()
        {
            InitializeComponent();
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            LogMessage("Window loaded, initializing terminal...");
            
            // Create the terminal instance (aligns with template)
            _terminal = new GUIConsole.ConPTY.Terminal();
            
            // Subscribe to output ready event
            _terminal.OutputReady += Terminal_OutputReady;
            
            // Start terminal in background thread (IMPORTANT: Start blocks until process exits)
            Task.Run(() => {
                try 
                {
                    LogMessage("Starting PowerShell...");
                    // This will block until the process exits!
                    _terminal.Start("powershell.exe -NoProfile -NoExit");
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
            
            // Get pipe details immediately
            Dispatcher.Invoke(() => {
                CheckPipeStatus();
            });
            
            // Create cancellation token for reading operations
            _readCancellation = new CancellationTokenSource();
            
            // Try multiple reading approaches to see what works
            StartStreamReading();
            StartDirectReading();
            
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

        // METHOD 1: Standard Stream Reading
        private void StartStreamReading()
        {
            Task.Run(() => {
                LogMessage("Starting Stream.Read method...");
                byte[] buffer = new byte[4096];
                
                try
                {
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
                                LogMessage($"Text: {text.Replace("\r", "\\r").Replace("\n", "\\n")}");
                                
                                // Update UI with text
                                Dispatcher.Invoke(() => {
                                    AppendOutputText(text);
                                });
                            }
                            else
                            {
                                Thread.Sleep(100);
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
        
        // METHOD 2: Direct Win32 ReadFile API
        private void StartDirectReading()
        {
            Task.Run(() => {
                LogMessage("Starting direct ReadFile method...");
                byte[] buffer = new byte[4096];
                
                try
                {
                    while (!_readCancellation.IsCancellationRequested)
                    {
                        try
                        {
                            if (_terminal.ConsoleOutStream?.SafeFileHandle == null || 
                                _terminal.ConsoleOutStream.SafeFileHandle.IsInvalid)
                            {
                                LogMessage("File handle is invalid");
                                Thread.Sleep(500);
                                continue;
                            }
                            
                            // Use Win32 ReadFile API directly
                            uint bytesRead = 0;
                            bool success = ReadFile(
                                _terminal.ConsoleOutStream.SafeFileHandle,
                                buffer,
                                (uint)buffer.Length,
                                ref bytesRead,
                                IntPtr.Zero);
                            
                            if (success && bytesRead > 0)
                            {
                                string text = Encoding.UTF8.GetString(buffer, 0, (int)bytesRead);
                                LogMessage($"ReadFile: {bytesRead} bytes");
                                LogMessage($"Text: {text.Replace("\r", "\\r").Replace("\n", "\\n")}");
                                
                                // Update UI with text
                                Dispatcher.Invoke(() => {
                                    AppendOutputText(text);
                                });
                            }
                            else
                            {
                                // Check for specific errors if needed
                                if (!success)
                                {
                                    int error = Marshal.GetLastWin32Error();
                                    if (error != 0)
                                    {
                                        LogMessage($"ReadFile error code: {error}");
                                    }
                                }
                                
                                Thread.Sleep(100);
                            }
                        }
                        catch (Exception ex)
                        {
                            LogMessage($"ReadFile error: {ex.Message}");
                            Thread.Sleep(500);
                        }
                        
                        // Periodically check pipe status
                        if (DateTime.Now.Second % 5 == 0)
                        {
                            CheckPipeStatus();
                        }
                    }
                }
                catch (Exception ex)
                {
                    LogMessage($"Direct reading thread error: {ex.Message}");
                }
                
                LogMessage("Direct reading thread exited");
            }, _readCancellation.Token);
        }
        
        private void CheckPipeStatus()
        {
            try
            {
                if (_terminal?.ConsoleOutStream == null)
                {
                    LogMessage("ConsoleOutStream is null");
                    return;
                }
                
                SafeFileHandle handle = _terminal.ConsoleOutStream.SafeFileHandle;
                if (handle == null || handle.IsInvalid || handle.IsClosed)
                {
                    LogMessage("Pipe handle is null, invalid, or closed");
                    return;
                }
                
                LogMessage($"Pipe check: CanRead={_terminal.ConsoleOutStream.CanRead}, " +
                           $"Position={_terminal.ConsoleOutStream.Position}, " +
                           $"SafeFileHandle.IsInvalid={handle.IsInvalid}");
                
                // Check if data is available in the pipe
                uint bytesRead = 0;
                uint totalBytesAvail = 0;
                uint bytesLeftThisMessage = 0;
                
                bool success = PeekNamedPipe(
                    handle,
                    null,
                    0,
                    ref bytesRead,
                    ref totalBytesAvail,
                    ref bytesLeftThisMessage);
                
                if (success)
                {
                    LogMessage($"PeekNamedPipe: {totalBytesAvail} bytes available");
                    
                    // If data is available, we could try reading it immediately
                    if (totalBytesAvail > 0)
                    {
                        LogMessage("Data available in pipe!");
                    }
                }
                else
                {
                    int error = Marshal.GetLastWin32Error();
                    LogMessage($"PeekNamedPipe failed with error {error}");
                }
            }
            catch (Exception ex)
            {
                LogMessage($"CheckPipeStatus error: {ex.Message}");
            }
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
                AppendOutputText($"\r\n> {command}\r\n");
                
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
        
        private void AppendOutputText(string text)
        {
            // Add the text to the output
            OutputTextBox.AppendText(text);
            
            // Auto-scroll if enabled
            if (_autoScroll)
            {
                OutputTextBox.ScrollToEnd();
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