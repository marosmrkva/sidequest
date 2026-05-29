using Microsoft.Data.Sqlite;
using Microsoft.Win32;
using System.Collections;
using System.Collections.ObjectModel;
using System.Dynamic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace Sidequest
{
    public partial class MainWindow : Window
    {
        private static string dbPath;

        public ObservableCollection<Quest> listQuests { get; set; }
        public ObservableCollection<Quest> listFinishedQuests { get; set; }
        public ObservableCollection<Quest> listOverdueQuests { get; set; }

        public bool isAnimating = false;
        public bool canResize = false;

        private double _anchorRight;
        private double _anchorBottom;

        public static bool isMouseInside = false;

        public static string SaveFile = @"quests_savefile.txt";

        bool isCollapsed = true;
        bool isQuestEntryOpen = false;

        private void SetStartup(bool enable)
        {
            string runKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
            string appName = "Sidequest";

            string exePath = Environment.ProcessPath;

            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(runKey, true))
            {
                if (enable)
                {
                    key.SetValue(appName, $"\"{exePath}\"");
                }
                else
                {
                    key.DeleteValue(appName, false);
                }
            }
        }

        
        
        private void Timer_Tick(object sender, EventArgs e)
        {
            CheckDeadlines();
        }
        
        

        private void InitializeDatabase()
        {
            using (var connection = new SqliteConnection(dbPath))
            {
                connection.Open();

                var command = connection.CreateCommand();

                command.CommandText = @"
                    CREATE TABLE IF NOT EXISTS Quests (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        QuestName TEXT NOT NULL,
                        Deadline TEXT,
                        Content TEXT,
                        Status BOOLEAN
                    )";

                command.ExecuteNonQuery();
            }
        }

        public void CheckDeadlines()
        {
            LoadQuestsFromDatabase();
            
            List<Quest> sortedDeadlines = new List<Quest>();

            using (var connection = new SqliteConnection(dbPath))
            {
                connection.Open();

                var command = connection.CreateCommand();

                command.CommandText = "SELECT Id, QuestName, Deadline, Content, Status FROM Quests ORDER BY Deadline ASC";

                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        Quest tempQuest = new Quest();
                        tempQuest.ID = reader.GetInt32(0);
                        tempQuest.QuestName = reader.GetString(1);
                        tempQuest.Deadline = Convert.ToDateTime(reader.GetString(2));
                        tempQuest.QuestContents = reader.GetString(3);
                        tempQuest.IsCompleted = reader.GetBoolean(4);

                        sortedDeadlines.Add(tempQuest);
                    }
                }

                if (sortedDeadlines.Count == 0) return;

                TimeSpan span = (sortedDeadlines[0].Deadline).Subtract(DateTime.Now);

                if (sortedDeadlines[0].ID == null) return;

                
                while (sortedDeadlines.Count > 0)
                {
                    if (span.TotalSeconds < 0 && !sortedDeadlines[0].IsCompleted)
                    {
                        Quest overdueQuest = null;
                        foreach (Quest quest in listQuests)
                        {
                            if (quest.ID == sortedDeadlines[0].ID) overdueQuest = quest;
                        }
                        if (overdueQuest != null) listQuests.Remove(overdueQuest);
                        listOverdueQuests.Add(sortedDeadlines[0]);
                        
                        span = (sortedDeadlines[0].Deadline).Subtract(DateTime.Now);
                    }
                    else break;
                    sortedDeadlines.Remove(sortedDeadlines[0]);
                }
                
                if (span.Hours <= 24)
                {
                    if (isCollapsed)
                    {
                        MainBorder.BorderBrush = new SolidColorBrush(Colors.Red);
                        return;
                    }
                }

                MainBorder.BorderBrush = (SolidColorBrush) new BrushConverter().ConvertFrom("#2a2a2a");
            }
        }

        private void LoadQuestsFromDatabase()
        {
            listQuests.Clear();
            listFinishedQuests.Clear();
            listOverdueQuests.Clear();

            using (var connection = new SqliteConnection(dbPath))
            {
                connection.Open();

                var command = connection.CreateCommand();

                command.CommandText = "SELECT Id, QuestName, Deadline, Content, Status FROM Quests ORDER BY Deadline ASC";

                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        Quest loadedQuest = new Quest();

                        loadedQuest.ID = reader.GetInt32(0);
                        loadedQuest.QuestName = reader.GetString(1);
                        string deadlineString = reader.GetString(2);
                        loadedQuest.Deadline = Convert.ToDateTime(deadlineString);
                        loadedQuest.QuestContents = reader.GetString(3);
                        loadedQuest.IsCompleted = reader.GetBoolean(4);

                        if (!loadedQuest.IsCompleted) listQuests.Add(loadedQuest);
                        else listFinishedQuests.Add(loadedQuest);
                    }
                }
            }
        }

        

        private void Button_Click(object sender, RoutedEventArgs e)
        {
            if (!isQuestEntryOpen)
            {
                NewQuestEntryTextBox.Text = "";
                NewQuestContentTextBox.Text = "";
                NewQuestEntry.Visibility = Visibility.Visible;
                isQuestEntryOpen = true;
            }
            else
            {
                NewQuestEntry.Visibility = Visibility.Collapsed;
                isQuestEntryOpen = false;
            }
                
        }

        private void SaveNewQuest(object sender, RoutedEventArgs e)
        {
            string newQuestName = NewQuestEntryTextBox.Text;
            if (string.IsNullOrWhiteSpace(newQuestName)) return;

            DateTime newQuestDeadline;
            if (NewQuestDeadlineDate.SelectedDate != null)
            {
                TimeSpan span = NewQuestDeadlineDate.SelectedDate.Value.Subtract(DateTime.Now);

                if (span.Hours < 0) return;
                newQuestDeadline = NewQuestDeadlineDate.SelectedDate.Value;
                
            }
            else
            {
                newQuestDeadline = DateTime.Now.AddDays(1);
            }

            string newQuestContent = NewQuestContentTextBox.Text;

            Quest newQuest = new Quest();
            newQuest.QuestName = newQuestName;
            newQuest.Deadline = newQuestDeadline;
            newQuest.QuestContents = newQuestContent;
            listQuests.Add(newQuest);

            using (var connection = new SqliteConnection(dbPath))
            {
                connection.Open();
                var command = connection.CreateCommand();
                command.CommandText = "INSERT INTO Quests (QuestName, Deadline, Content, Status) VALUES (@name, @date, @content, @status)";

                command.Parameters.AddWithValue("@name", newQuestName);
                command.Parameters.AddWithValue("@date", newQuestDeadline.ToString("s"));
                command.Parameters.AddWithValue("@content", newQuestContent);
                command.Parameters.AddWithValue("@status", false);

                command.ExecuteNonQuery();
            }

            NewQuestEntryTextBox.Text = "";
            NewQuestContentTextBox.Text = "";
            NewQuestDeadlineDate.SelectedDate = null;

            NewQuestEntry.Visibility = Visibility.Collapsed;

            CheckDeadlines();
        }

        private void FinishQuest(object sender, RoutedEventArgs e)
        {
            Button pressedButton = sender as Button;
            Quest QuestToFinish = pressedButton.DataContext as Quest;

            if (QuestToFinish == null) return;

            using (var connection = new SqliteConnection(dbPath))
            {
                connection.Open();

                var command = connection.CreateCommand();

                command.CommandText = "UPDATE Quests SET Status = true WHERE Id = @id";
                command.Parameters.AddWithValue("@id", QuestToFinish.ID);

                command.ExecuteNonQuery();
            }

            listQuests.Remove(QuestToFinish);
            listFinishedQuests.Add(QuestToFinish);
        }

        private void FinishOverdueQuest(object sender, RoutedEventArgs e)
        {
            Button pressedButton = sender as Button;
            Quest QuestToFinish = pressedButton.DataContext as Quest;

            if (QuestToFinish == null) return;

            using (var connection = new SqliteConnection(dbPath))
            {
                connection.Open();

                var command = connection.CreateCommand();

                command.CommandText = "UPDATE Quests SET Status = true WHERE Id = @id";
                command.Parameters.AddWithValue("@id", QuestToFinish.ID);

                command.ExecuteNonQuery();
            }

            listOverdueQuests.Remove(QuestToFinish);
            listFinishedQuests.Add(QuestToFinish);
        }

        private void RemoveQuest(object sender, RoutedEventArgs e)
        {
            Button pressedButton = sender as Button;
            Quest QuestToRemove = pressedButton.DataContext as Quest;

            if (QuestToRemove == null) return;

            using (var connection = new SqliteConnection(dbPath))
            {
                connection.Open();

                var command = connection.CreateCommand();

                command.CommandText = "DELETE FROM Quests WHERE Id = @id";
                command.Parameters.AddWithValue("@id", QuestToRemove.ID);

                command.ExecuteNonQuery();
            }
            listQuests.Remove(QuestToRemove);
            listFinishedQuests.Remove(QuestToRemove);

            CheckDeadlines();
        }

        private void Window_Expand(object sender, MouseEventArgs e)
        {
            isMouseInside = true;
            isCollapsed = false;
            animateWindow(300);
            
        }

        private async void Window_Collapse(object sender, MouseEventArgs e)
        {
            isMouseInside = false;
            await Task.Delay(500);
            if (isMouseInside) return;
            isCollapsed = true;
            CheckDeadlines();
            animateWindow(40);
        }


        private void animateProperty(DependencyProperty prop, double targetSize)
        {
            DoubleAnimation sizeAnim = new DoubleAnimation()
            {
                To = targetSize,
                Duration = TimeSpan.FromMilliseconds(200),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut}
            };
            this.BeginAnimation(prop, sizeAnim, HandoffBehavior.Compose);
        }

        private async Task animateWindow(double targetSize)
        {
            if (canResize && targetSize == 40)
            {
                await Task.Delay(5000);
                canResize = false;
                MainGrid.Visibility = Visibility.Collapsed;
            }

            canResize = true;

            animateProperty(Window.WidthProperty, targetSize);
            animateProperty(Window.HeightProperty, targetSize);

            animateProperty(Window.LeftProperty, _anchorRight - targetSize);
            animateProperty(Window.TopProperty, _anchorBottom - targetSize);

            if (canResize && targetSize == 300) MainGrid.Visibility = Visibility.Visible;

            Thread.Sleep(250);
        }
        
        private void ShowActive(object sender, RoutedEventArgs e)
        {
            ActiveQuests.Visibility = Visibility.Visible;
            FinishedQuests.Visibility = Visibility.Collapsed;
            OverdueQuests.Visibility = Visibility.Collapsed;

            ActiveQuestsButton.BorderBrush = (SolidColorBrush) new BrushConverter().ConvertFrom("#2a2a2a");
            FinishedQuestsButton.BorderBrush = (SolidColorBrush)new BrushConverter().ConvertFrom("#121212");
            OverdueQuestsButton.BorderBrush = (SolidColorBrush)new BrushConverter().ConvertFrom("#121212");

        }

        private void ShowFinished(object sender, RoutedEventArgs e)
        {
            ActiveQuests.Visibility = Visibility.Collapsed;
            FinishedQuests.Visibility = Visibility.Visible;
            OverdueQuests.Visibility = Visibility.Collapsed;

            ActiveQuestsButton.BorderBrush = (SolidColorBrush)new BrushConverter().ConvertFrom("#121212");
            FinishedQuestsButton.BorderBrush = (SolidColorBrush)new BrushConverter().ConvertFrom("#2a2a2a");
            OverdueQuestsButton.BorderBrush = (SolidColorBrush)new BrushConverter().ConvertFrom("#121212");
        }

        private void ShowOverdue(object sender, RoutedEventArgs e)
        {
            ActiveQuests.Visibility = Visibility.Collapsed;
            FinishedQuests.Visibility = Visibility.Collapsed;
            OverdueQuests.Visibility = Visibility.Visible;
            ActiveQuestsButton.BorderBrush = (SolidColorBrush)new BrushConverter().ConvertFrom("#121212");
            FinishedQuestsButton.BorderBrush = (SolidColorBrush)new BrushConverter().ConvertFrom("#121212");
            OverdueQuestsButton.BorderBrush = (SolidColorBrush)new BrushConverter().ConvertFrom("#2a2a2a");

        }

        private void Button_Exit(object sender, RoutedEventArgs e)
        {
            Environment.Exit(0);
        }

        public MainWindow()
        {
            InitializeComponent();

            try
            {
                string appFolder = AppDomain.CurrentDomain.BaseDirectory;
                string fullDbPath = System.IO.Path.Combine(appFolder, "quests.db");

                dbPath = $"Data Source={fullDbPath}";

                listQuests = new ObservableCollection<Quest>();
                listFinishedQuests = new ObservableCollection<Quest>();
                listOverdueQuests = new ObservableCollection<Quest>();
                this.DataContext = this;

                var desktopWorkingArea = SystemParameters.WorkArea;

                this.Width = 40;
                this.Height = 40;

                _anchorRight = desktopWorkingArea.Right - 20;
                _anchorBottom = desktopWorkingArea.Bottom - 20;

                this.Left = _anchorRight - this.Width;
                this.Top = _anchorBottom - this.Height;

                MainGrid.Visibility = Visibility.Collapsed;

                InitializeDatabase();
                LoadQuestsFromDatabase();

                DispatcherTimer timer = new DispatcherTimer();
                timer.Interval = TimeSpan.FromSeconds(60);
                timer.Tick += Timer_Tick;
                timer.Start();

                CheckDeadlines();

                SetStartup(true);
                ActiveQuestsButton.BorderBrush = (SolidColorBrush)new BrushConverter().ConvertFrom("#2a2a2a");
            }
            catch (Exception ex)
            {
                string desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
                string logFile = System.IO.Path.Combine(desktopPath, "SidequestError.txt");
                System.IO.File.WriteAllText(logFile, "CHYBA PRI ŠTARTE:\n" + ex.ToString());
            }
        }
    }
}