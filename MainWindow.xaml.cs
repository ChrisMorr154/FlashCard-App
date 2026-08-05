using CsvHelper;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace FlashCardApp
{
    public partial class MainWindow : Window
    {
        private int currentCardIndex = 0;
        private List<FlashCard> flashCards = new List<FlashCard>();
        private bool isShowingAnswer = false;
        private bool isLoadingDecks = false;

        public MainWindow()
        {
            InitializeComponent();

            LoadDeckList();

            if (DatabaseExists())
            {
                LoadFlashCardsFromDatabase();
                DisplayCard();
            }
            else
            {
                Card.Text = "Cards: \n\n(EMPTY).";
            }
        }

        private string GetAppFolder()
        {
            string appFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "FlashCardApp");

            Directory.CreateDirectory(appFolder);

            return appFolder;
        }

        private void LoadDeckList()
        {
            isLoadingDecks = true;
            string appFolder = GetAppFolder();

            ComboBoxDecks.Items.Clear();

            string[] databases = Directory.GetFiles(appFolder, "*.db");

            foreach (string database in databases)
            {
                ComboBoxDecks.Items.Add(Path.GetFileName(database));
            }


            // Select current deck
            string currentDeckFile = Path.Combine(
                appFolder,
                "currentdeck.txt");


            if (File.Exists(currentDeckFile))
            {
                string currentDeck = File.ReadAllText(currentDeckFile);

                ComboBoxDecks.SelectedItem = currentDeck;
            }
            isLoadingDecks = false;
        }

        private bool DatabaseExists()
        {
            string appFolder = GetAppFolder();

            string settingsFile = Path.Combine(appFolder, "currentdeck.txt");

            if (!File.Exists(settingsFile))
                return false;

            string currentDeck = File.ReadAllText(settingsFile);

            return File.Exists(Path.Combine(appFolder, currentDeck));
        }

        private void LoadFlashCardsFromDatabase()
        {
            try
            {
                string appFolder = GetAppFolder();

                string currentDeck = File.ReadAllText(
                    Path.Combine(appFolder, "currentdeck.txt"));

                string dbPath = Path.Combine(appFolder, currentDeck);

                string connectionString = $"Data Source={dbPath};Version=3;";

                using (var connection = new SQLiteConnection(connectionString))
                {
                    connection.Open();

                    string query = "SELECT * FROM FlashCards";

                    using (var command = new SQLiteCommand(query, connection))
                    using (var reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            flashCards.Add(new FlashCard
                            {
                                Question = reader["Question"]?.ToString(),
                                Answer = reader["Answer"]?.ToString(),
                                Hint = reader["Hint"]?.ToString()
                            });
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.ToString());
            }
        }

        //settings
        private bool settingsOpen = false;

        private void ButtonSettings_Click(object sender, RoutedEventArgs e)
        {
            DoubleAnimation animation = new DoubleAnimation
            {
                Duration = TimeSpan.FromMilliseconds(250),
                EasingFunction = new CubicEase
                {
                    EasingMode = EasingMode.EaseOut
                }
            };

            if (!settingsOpen)
            {
                Overlay.Visibility = Visibility.Visible;

                animation.From = 320;
                animation.To = 0;
            }
            else
            {
                animation.From = 0;
                animation.To = 320;

                animation.Completed += (s, a) =>
                {
                    Overlay.Visibility = Visibility.Collapsed;
                };
            }

            SettingsTransform.BeginAnimation(
                TranslateTransform.XProperty,
                animation);

            settingsOpen = !settingsOpen;
        }

        private void CloseSettings_Click(object sender, RoutedEventArgs e)
        {
            ButtonSettings_Click(sender, e);
        }

        private void ButtonImportCards_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog dialog = new OpenFileDialog();

            dialog.Filter =
                "Supported Files (*.db;*.csv)|*.db;*.csv|" +
                "SQLite Database (*.db)|*.db|" +
                "CSV Files (*.csv)|*.csv|" +
                "All Files (*.*)|*.*";

            if (DatabaseExists())
            {
                MessageBoxResult result = MessageBox.Show(
                    "Importing a new deck will replace the current deck. Continue?",
                    "Replace Deck",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (result != MessageBoxResult.Yes)
                    return;
            }

            if (dialog.ShowDialog() == true)
            {
                string selectedFile = dialog.FileName;

                string extension = Path.GetExtension(selectedFile).ToLower();

                if (extension == ".db")
                {
                    ImportDatabase(selectedFile);
                }
                else if (extension == ".csv")
                {
                    ImportCsv(selectedFile);
                }
            }
        }

        private void ImportDatabase(string selectedFile)
        {
            string fileName = Path.GetFileName(selectedFile);

            string appFolder = GetAppFolder();

            string destination = Path.Combine(appFolder, fileName);

            File.Copy(selectedFile, destination, true);

            File.WriteAllText(
                Path.Combine(appFolder, "currentdeck.txt"),
                fileName);

            // Reload cards
            flashCards.Clear();
            currentCardIndex = 0;
            isShowingAnswer = false;

            LoadFlashCardsFromDatabase();
            DisplayCard();

            MessageBox.Show("Deck imported successfully!");
            LoadDeckList();
        }

        private void ImportCsv(string csvFile)
        {
            string appFolder = GetAppFolder();

            string dbName = Path.GetFileNameWithoutExtension(csvFile) + ".db";
            string dbPath = Path.Combine(appFolder, dbName);

            // Replace existing database if it already exists.
            if (File.Exists(dbPath))
            {
                File.Delete(dbPath);
            }

            SQLiteConnection.CreateFile(dbPath);

            string connectionString = $"Data Source={dbPath};Version=3;";

            using (var connection = new SQLiteConnection(connectionString))
            {
                connection.Open();

                string createTable = @"
            CREATE TABLE FlashCards
            (
                Question TEXT,
                Answer TEXT,
                Hint TEXT
            );";

                using (var command = new SQLiteCommand(createTable, connection))
                {
                    command.ExecuteNonQuery();
                }

                string insert = @"
            INSERT INTO FlashCards
            (Question, Answer, Hint)
            VALUES
            (@Question, @Answer, @Hint);";

                using (var command = new SQLiteCommand(insert, connection))
                {
                    command.Parameters.Add("@Question", System.Data.DbType.String);
                    command.Parameters.Add("@Answer", System.Data.DbType.String);
                    command.Parameters.Add("@Hint", System.Data.DbType.String);

                    using (var reader = new StreamReader(csvFile))
                    using (var csv = new CsvReader(reader, CultureInfo.InvariantCulture))
                    {
                        var records = csv.GetRecords<FlashCard>();

                        foreach (var card in records)
                        {
                            command.Parameters["@Question"].Value = card.Question;
                            command.Parameters["@Answer"].Value = card.Answer;
                            command.Parameters["@Hint"].Value = card.Hint;

                            command.ExecuteNonQuery();
                        }
                    }
                }
            }

            File.WriteAllText(
                Path.Combine(appFolder, "currentdeck.txt"),
                dbName);

            flashCards.Clear();
            currentCardIndex = 0;
            isShowingAnswer = false;

            LoadFlashCardsFromDatabase();
            DisplayCard();

            LoadDeckList();

            MessageBox.Show("CSV imported successfully!");
        }

        private void ComboBoxDecks_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (isLoadingDecks)
                return;

            if (ComboBoxDecks.SelectedItem == null)
                return;

            string selectedDeck = ComboBoxDecks.SelectedItem.ToString();

            string appFolder = GetAppFolder();

            File.WriteAllText(
                Path.Combine(appFolder, "currentdeck.txt"),
                selectedDeck);


            flashCards.Clear();
            currentCardIndex = 0;
            isShowingAnswer = false;

            LoadFlashCardsFromDatabase();

            DisplayCard();
        }

        private void DisplayCard()
        {
            if (flashCards.Count == 0)
            {
                MessageBox.Show("No flashcards found.");
                return;
            }

            var currentCard = flashCards[currentCardIndex];

            if (isShowingAnswer)
            {
                Card.Text = currentCard.Answer;
            }
            else
            {
                Card.Text = currentCard.Question;
            }
        }


        private async void ButtonAnswer_Click(object sender, RoutedEventArgs e)
        {
            isShowingAnswer = !isShowingAnswer;

            var current = flashCards[currentCardIndex];

            await FlipCardAnswer(
                isShowingAnswer
                    ? current.Answer
                    : current.Question);
        }

        private async void ButtonNext_Click(object sender, RoutedEventArgs e)
        {
            currentCardIndex = (currentCardIndex + 1) % flashCards.Count;
            isShowingAnswer = false;

            await FlipCardAni(flashCards[currentCardIndex].Question);
        }

        private async void ButtonPrevious_Click(object sender, RoutedEventArgs e)
        {
            currentCardIndex = (currentCardIndex - 1 + flashCards.Count) % flashCards.Count;
            isShowingAnswer = false;

            await FlipCardAni(flashCards[currentCardIndex].Question);
        }

        private void ButtonHint_Click(object sender, RoutedEventArgs e)
        {
            var currentCard = flashCards[currentCardIndex];
            MessageBox.Show(currentCard.Hint, "Hint");
        }

        private bool darkMode = false;

        private void ButtonLight_Click(object sender, RoutedEventArgs e)
        {
            darkMode = false;

            // Toggle
            LightToggle.FontSize = 18;
            LightToggle.FontWeight = FontWeights.Bold;
            LightToggle.Opacity = 1;
            DarkToggle.FontSize = 15;
            DarkToggle.FontWeight = FontWeights.Normal;
            DarkToggle.Opacity = 0.55;

            Background = Brushes.White;

            // Main Panels
            LeftPanel.Background = Brushes.White;
            LeftPanel.BorderBrush = Brushes.White;
            RightPanel.Background = Brushes.White;
            RightPanel.BorderBrush = Brushes.White;

            // Card
            RealCard.Background = new SolidColorBrush(Color.FromRgb(249, 250, 251));
            RealCard.BorderBrush = new SolidColorBrush(Color.FromRgb(229, 231, 235));

            // Right Panel
            SettingsPanel.Background = new SolidColorBrush(Color.FromRgb(249, 250, 251));

            // Light Button Styles
            PreviousButton.Style = (Style)FindResource("AnswerButton");
            HintButton.Style = (Style)FindResource("AnswerButton");
            NextButton.Style = (Style)FindResource("AnswerButton");
            FlipCard.Style = (Style)FindResource("AnswerButton");
            ImportBorder.Style = (Style)FindResource("ImportButton");
            ShuffleBorder.Style = (Style)FindResource("ImportButton");
            SettingBorder.Style = (Style)FindResource("ImportButton");
            ComboBoxDecks.Style = (Style)FindResource("DropDownBox");

            // DropDown Color
            ComboBoxDecks.Background = Brushes.White;
            Card.Foreground = Brushes.Black;
            ComboBoxDecks.Foreground = Brushes.Black;
            ComboBoxDecks.BorderBrush = Brushes.LightGray;

            // title bar style
            TitleBar.Background = Brushes.White;
            Mini.Foreground = Brushes.Black;
            Exit.Foreground = Brushes.Black;
            Exit.BorderBrush = Brushes.White;
            Mini.BorderBrush = Brushes.White;
            title.Foreground = Brushes.Black;
        }

        private void ButtonDark_Click(object sender, RoutedEventArgs e)
        {
            darkMode = true;

            // Toggle
            DarkToggle.FontSize = 18;
            DarkToggle.FontWeight = FontWeights.Bold;
            DarkToggle.Foreground = Brushes.White;
            DarkToggle.Opacity = 1;

            LightToggle.FontSize = 15;
            LightToggle.FontWeight = FontWeights.Normal;
            LightToggle.Foreground = new SolidColorBrush(Color.FromRgb(179, 179, 179));
            LightToggle.Opacity = 0.7;

            // Window
            Background = new SolidColorBrush(Color.FromRgb(18, 18, 18));

            // Panels
            var panel = new SolidColorBrush(Color.FromRgb(30, 30, 30)); 
            LeftPanel.Background = panel;
            RightPanel.Background = panel;
            LeftPanel.BorderBrush = new SolidColorBrush(Color.FromRgb(58, 58, 58));
            RightPanel.BorderBrush = new SolidColorBrush(Color.FromRgb(58, 58, 58));

            // Flash card
            RealCard.Background = new SolidColorBrush(Color.FromRgb(36, 36, 36));
            RealCard.BorderBrush = new SolidColorBrush(Color.FromRgb(58, 58, 58));

            // Settings
            SettingsPanel.Background = panel;
            SettingsPanel.BorderBrush = new SolidColorBrush(Color.FromRgb(58, 58, 58));

            // ComboBox
            ComboBoxDecks.Background = Brushes.Gray;
            ComboBoxDecks.Foreground = Brushes.White;
            ComboBoxDecks.BorderBrush = Brushes.Black;

            // Text
            Card.Foreground = Brushes.White;

            // Buttons
            ImportBorder.Style = (Style)FindResource("DarkImportButton");
            ShuffleBorder.Style = (Style)FindResource("DarkImportButton");
            SettingBorder.Style = (Style)FindResource("DarkImportButton");
            PreviousButton.Style = (Style)FindResource("DarkPrimaryButton");
            HintButton.Style = (Style)FindResource("DarkPrimaryButton");
            NextButton.Style = (Style)FindResource("DarkPrimaryButton");
            FlipCard.Style = (Style)FindResource("DarkPrimaryButton");
            ComboBoxDecks.Style = (Style)FindResource("DarkDropDownBox");

            // title bar style
            TitleBar.Background = Brushes.Black;
            Mini.Foreground = Brushes.White;
            Mini.BorderBrush = Brushes.Black;
            Exit.Foreground = Brushes.White;
            Exit.BorderBrush = Brushes.Black;
            title.Foreground = Brushes.White;
        }

        private void ButtonShuffle_Click(object sender, RoutedEventArgs e)
        {
            flashCards.Shuffle();

            currentCardIndex = 0;
            isShowingAnswer = false;

            DisplayCard();
        }

        private async Task FlipCardAni(string newText)
        {
            var hide = new DoubleAnimation
            {
                To = 0,
                Duration = TimeSpan.FromMilliseconds(150),
                EasingFunction = new CubicEase
                {
                    EasingMode = EasingMode.EaseIn
                }
            };

            var tcs = new TaskCompletionSource<bool>();

            hide.Completed += (s, e) =>
            {
                Card.Text = newText;
                tcs.SetResult(true);
            };

            CardFlip.BeginAnimation(
                ScaleTransform.ScaleXProperty,
                hide);

            await tcs.Task;


            var show = new DoubleAnimation
            {
                To = 1,
                Duration = TimeSpan.FromMilliseconds(150),
                EasingFunction = new CubicEase
                {
                    EasingMode = EasingMode.EaseOut
                }
            };

            CardFlip.BeginAnimation(
                ScaleTransform.ScaleXProperty,
                show);
        }

        private async Task FlipCardAnswer(string newText)
        {
            var hide = new DoubleAnimation
            {
                To = 0,
                Duration = TimeSpan.FromMilliseconds(150),
                EasingFunction = new CubicEase
                {
                    EasingMode = EasingMode.EaseIn
                }
            };

            var tcs = new TaskCompletionSource<bool>();

            hide.Completed += (s, e) =>
            {
                Card.Text = newText;
                tcs.SetResult(true);
            };

            CardFlip.BeginAnimation(
                ScaleTransform.ScaleYProperty,
                hide);

            await tcs.Task;


            var show = new DoubleAnimation
            {
                To = 1,
                Duration = TimeSpan.FromMilliseconds(150),
                EasingFunction = new CubicEase
                {
                    EasingMode = EasingMode.EaseOut
                }
            };

            CardFlip.BeginAnimation(
                ScaleTransform.ScaleYProperty,
                show);
        }

        // title bar methods
        private void TitleBar_Drag(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2)
            {
                WindowState = WindowState == WindowState.Maximized
                    ? WindowState.Normal : WindowState.Minimized;
            }
            else
            {
                DragMove();
            }
        }

        private void Minimize_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

    }

    public class FlashCard
    {
        public string? Question { get; set; } = string.Empty;
        public string? Answer { get; set; } = string.Empty;
        public string? Hint { get; set; } = string.Empty;
    }

    public static class Extensions
    {
        private static Random rng = new Random();

        public static void Shuffle<T>(this IList<T> list)
        {
            int n = list.Count;
            while (n > 1)
            {
                n--;
                int k = rng.Next(n + 1);
                T value = list[k];
                list[k] = list[n];
                list[n] = value;
            }
        }

    }
}