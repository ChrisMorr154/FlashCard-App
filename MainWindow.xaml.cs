using CsvHelper;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
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
                TextBlockQuestionAnswer.Text = "Cards: \n\n(EMPTY).";
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
                TextBlockQuestionAnswer.Text = currentCard.Answer;
            }
            else
            {
                TextBlockQuestionAnswer.Text = currentCard.Question;
            }
        }


        private void ButtonAnswer_Click(object sender, RoutedEventArgs e)
        {
            isShowingAnswer = !isShowingAnswer;
            DisplayCard();
        }

        private void ButtonNext_Click(object sender, RoutedEventArgs e)
        {
            currentCardIndex = (currentCardIndex + 1) % flashCards.Count;
            isShowingAnswer = false;
            DisplayCard();
        }

        private void ButtonPrevious_Click(object sender, RoutedEventArgs e)
        {
            currentCardIndex = (currentCardIndex - 1 + flashCards.Count) % flashCards.Count;
            isShowingAnswer = false;
            DisplayCard();
        }

        private void ButtonHint_Click(object sender, RoutedEventArgs e)
        {
            var currentCard = flashCards[currentCardIndex];
            MessageBox.Show(currentCard.Hint, "Hint");
        }

        private void ButtonShuffle_Click(object sender, RoutedEventArgs e)
        {
            flashCards.Shuffle();

            currentCardIndex = 0;
            isShowingAnswer = false;

            DisplayCard();
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