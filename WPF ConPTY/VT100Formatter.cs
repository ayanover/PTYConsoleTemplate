﻿using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace WPF_ConPTY
{
    /// <summary>
    /// Processes VT100 escape sequences and applies them to a RichTextBox
    /// </summary>
    public class VT100Formatter
    {
        // Current state
        private int _currentRow = 0;
        private int _currentCol = 0;
        private SolidColorBrush _currentForeground = Brushes.White;
        private SolidColorBrush _currentBackground = Brushes.Black;
        private bool _isBold = false;
        private bool _isUnderline = false;
        private bool _isItalic = false;

        // Rich text box to update
        private RichTextBox _textBox;

        // Standard VT100 colors (0-15)
        private static readonly SolidColorBrush[] StandardColors = new SolidColorBrush[]
        {
            Brushes.Black,           // 0: Black
            Brushes.DarkRed,         // 1: Dark Red
            Brushes.DarkGreen,       // 2: Dark Green
            Brushes.DarkGoldenrod,   // 3: Dark Yellow
            Brushes.DarkBlue,        // 4: Dark Blue
            Brushes.DarkMagenta,     // 5: Dark Magenta
            Brushes.DarkCyan,        // 6: Dark Cyan
            Brushes.LightGray,       // 7: Light Gray
            Brushes.DarkGray,        // 8: Dark Gray
            Brushes.Red,             // 9: Red
            Brushes.Green,           // 10: Green
            Brushes.Yellow,          // 11: Yellow
            Brushes.Blue,            // 12: Blue
            Brushes.Magenta,         // 13: Magenta
            Brushes.Cyan,            // 14: Cyan
            Brushes.White            // 15: White
        };

        // Regex to find VT100 sequences with or without the ESC character
        private static readonly Regex EscapeSequenceRegex = new Regex(
            @"(?:\x1B\[|\[)([0-9;?]*)([a-zA-Z@])",
            RegexOptions.Compiled);

        // Track cursor visibility
        private bool _cursorVisible = true;

        // Keep track of the current paragraph for better text handling
        private Paragraph _currentParagraph;

        public VT100Formatter(RichTextBox textBox)
        {
            _textBox = textBox;

            // Set up the text box for terminal-like behavior
            _textBox.Document.LineHeight = 1.0;
            _textBox.FontFamily = new FontFamily("Consolas");
            _textBox.Background = Brushes.Black;
            _textBox.Foreground = Brushes.White;
            _textBox.IsReadOnly = true;
            _textBox.AcceptsReturn = true;

            // Clear the document to start
            _textBox.Document.Blocks.Clear();

            // Create first paragraph
            _currentParagraph = new Paragraph();
            _textBox.Document.Blocks.Add(_currentParagraph);
        }

        /// <summary>
        /// Process and append text with VT100 sequences to the RichTextBox
        /// </summary>
        public void ProcessText(string text)
        {
            // First, pre-process common PowerShell sequences that don't follow standard VT100 format
            text = PreProcessPowerShellOutput(text);

            // Process the text and extract/handle escape sequences
            int lastIndex = 0;
            foreach (Match match in EscapeSequenceRegex.Matches(text))
            {
                // Add text before this escape sequence
                string textBeforeEscape = text.Substring(lastIndex, match.Index - lastIndex);
                if (!string.IsNullOrEmpty(textBeforeEscape))
                {
                    AppendText(textBeforeEscape);
                }

                // Process the escape sequence
                string parameters = match.Groups[1].Value;
                char command = match.Groups[2].Value[0];

                ProcessEscapeSequence(parameters, command);

                // Update last index
                lastIndex = match.Index + match.Length;
            }

            // Add any remaining text
            if (lastIndex < text.Length)
            {
                string remainingText = text.Substring(lastIndex);
                AppendText(remainingText);
            }
        }

        private string PreProcessPowerShellOutput(string text)
        {
            // Handle specific PowerShell output patterns

            // Handle PowerShell's color codes
            text = Regex.Replace(text, @"\[38;5;(\d+)m", m => {
                int colorIndex = int.Parse(m.Groups[1].Value);
                return $"\x1B[38;5;{colorIndex}m"; // Add ESC character back
            });

            // Handle cursor hide/show that might be missing ESC
            text = text.Replace("[?25l", "\x1B[?25l")
                       .Replace("[?25h", "\x1B[?25h");

            // Other specific PowerShell patterns
            text = text.Replace("]0;", "\x1B]0;");

            return text;
        }

        private void ProcessEscapeSequence(string parameters, char command)
        {
            string[] paramArray = !string.IsNullOrEmpty(parameters)
                ? parameters.Split(';')
                : new string[0];

            switch (command)
            {
                case 'm': // SGR - Select Graphic Rendition
                    ProcessSGR(paramArray);
                    break;

                case 'H': // CUP - Cursor Position
                case 'f': // HVP - Horizontal and Vertical Position
                    ProcessCursorPosition(paramArray);
                    break;

                case 'J': // ED - Erase Display
                    ProcessEraseDisplay(paramArray);
                    break;

                case 'K': // EL - Erase Line
                    ProcessEraseLine(paramArray);
                    break;

                case 'l': // Reset Mode
                case 'h': // Set Mode
                    ProcessMode(parameters, command);
                    break;
            }
        }

        private void ProcessMode(string parameters, char command)
        {
            // Handle special modes like cursor visibility
            if (parameters == "?25")
            {
                if (command == 'l') // Hide cursor
                    _cursorVisible = false;
                else if (command == 'h') // Show cursor
                    _cursorVisible = true;
            }
        }

        private void ProcessSGR(string[] parameters)
        {
            // If no parameters, reset all attributes
            if (parameters.Length == 0 || (parameters.Length == 1 && parameters[0] == "0"))
            {
                ResetAttributes();
                return;
            }

            for (int i = 0; i < parameters.Length; i++)
            {
                int param;
                if (!int.TryParse(parameters[i], out param))
                    continue;

                switch (param)
                {
                    case 0: // Reset all attributes
                        ResetAttributes();
                        break;

                    case 1: // Bold
                        _isBold = true;
                        break;

                    case 4: // Underline
                        _isUnderline = true;
                        break;

                    case 3: // Italic
                        _isItalic = true;
                        break;

                    // Foreground colors (30-37)
                    case 30:
                    case 31:
                    case 32:
                    case 33:
                    case 34:
                    case 35:
                    case 36:
                    case 37:
                        _currentForeground = StandardColors[param - 30];
                        break;

                    // Background colors (40-47)
                    case 40:
                    case 41:
                    case 42:
                    case 43:
                    case 44:
                    case 45:
                    case 46:
                    case 47:
                        _currentBackground = StandardColors[param - 40];
                        break;

                    // Bright foreground colors (90-97)
                    case 90:
                    case 91:
                    case 92:
                    case 93:
                    case 94:
                    case 95:
                    case 96:
                    case 97:
                        _currentForeground = StandardColors[param - 90 + 8];
                        break;

                    // Bright background colors (100-107)
                    case 100:
                    case 101:
                    case 102:
                    case 103:
                    case 104:
                    case 105:
                    case 106:
                    case 107:
                        _currentBackground = StandardColors[param - 100 + 8];
                        break;

                    // 256 colors with format: 38;5;n or 48;5;n
                    case 38:
                        if (i + 2 < parameters.Length && parameters[i + 1] == "5")
                        {
                            int colorIndex;
                            if (int.TryParse(parameters[i + 2], out colorIndex) && colorIndex >= 0 && colorIndex < 256)
                            {
                                _currentForeground = Get256Color(colorIndex);
                                i += 2; // Skip the next two parameters
                            }
                        }
                        break;

                    case 48:
                        if (i + 2 < parameters.Length && parameters[i + 1] == "5")
                        {
                            int colorIndex;
                            if (int.TryParse(parameters[i + 2], out colorIndex) && colorIndex >= 0 && colorIndex < 256)
                            {
                                _currentBackground = Get256Color(colorIndex);
                                i += 2; // Skip the next two parameters
                            }
                        }
                        break;
                }
            }
        }

        private void ProcessCursorPosition(string[] parameters)
        {
            // For basic cursor positioning, we can handle newlines 
            // and clear the current paragraph if it's a home position (1,1)
            int row = 1;
            int col = 1;

            if (parameters.Length >= 1 && !string.IsNullOrEmpty(parameters[0]))
                int.TryParse(parameters[0], out row);

            if (parameters.Length >= 2 && !string.IsNullOrEmpty(parameters[1]))
                int.TryParse(parameters[1], out col);

            // If it's a "Home" position (1,1), we can clear and restart
            if (row == 1 && col == 1)
            {
                _currentParagraph = new Paragraph();
                _textBox.Document.Blocks.Clear();
                _textBox.Document.Blocks.Add(_currentParagraph);
            }
        }

        private void ProcessEraseDisplay(string[] parameters)
        {
            int mode = 0;
            if (parameters.Length >= 1 && !string.IsNullOrEmpty(parameters[0]))
                int.TryParse(parameters[0], out mode);

            switch (mode)
            {
                case 0: // Clear from cursor to end of screen
                    // Simplified to just creating a new paragraph
                    _currentParagraph = new Paragraph();
                    _textBox.Document.Blocks.Add(_currentParagraph);
                    break;

                case 1: // Clear from cursor to beginning of screen
                    // Simplified approach
                    break;

                case 2: // Clear entire screen
                    _textBox.Document.Blocks.Clear();
                    _currentParagraph = new Paragraph();
                    _textBox.Document.Blocks.Add(_currentParagraph);
                    break;
            }
        }

        private void ProcessEraseLine(string[] parameters)
        {
            // Simplified line erasing - create a new paragraph
            _currentParagraph = new Paragraph();
            _textBox.Document.Blocks.Add(_currentParagraph);
        }

        private void ResetAttributes()
        {
            _currentForeground = Brushes.White;
            _currentBackground = Brushes.Black;
            _isBold = false;
            _isUnderline = false;
            _isItalic = false;
        }

        private void AppendText(string text)
        {
            // Skip if empty
            if (string.IsNullOrEmpty(text))
                return;

            // Create a formatted text run
            var textRun = new Run(text);

            // Apply current formatting
            textRun.Foreground = _currentForeground;
            textRun.Background = _currentBackground;

            if (_isBold)
                textRun.FontWeight = FontWeights.Bold;

            if (_isUnderline)
                textRun.TextDecorations = TextDecorations.Underline;

            if (_isItalic)
                textRun.FontStyle = FontStyles.Italic;

            // Get the current paragraph
            if (_currentParagraph == null || !_textBox.Document.Blocks.Contains(_currentParagraph))
            {
                _currentParagraph = new Paragraph();
                _textBox.Document.Blocks.Add(_currentParagraph);
            }

            // Add the text run
            _currentParagraph.Inlines.Add(textRun);

            // Auto-scroll if needed
            _textBox.ScrollToEnd();
        }

        // Generates colors for the 256-color palette
        private SolidColorBrush Get256Color(int index)
        {
            // First 16 colors are the standard colors
            if (index < 16)
                return StandardColors[index];

            // Colors 16-231 are a 6x6x6 color cube
            if (index < 232)
            {
                index -= 16;
                int r = (index / 36) * 51;
                int g = ((index / 6) % 6) * 51;
                int b = (index % 6) * 51;
                return new SolidColorBrush(Color.FromRgb((byte)r, (byte)g, (byte)b));
            }

            // Colors 232-255 are grayscale
            int gray = (index - 232) * 10 + 8;
            return new SolidColorBrush(Color.FromRgb((byte)gray, (byte)gray, (byte)gray));
        }
    }
}