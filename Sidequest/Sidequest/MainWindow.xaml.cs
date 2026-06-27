using Microsoft.Data.Sqlite;
using Microsoft.Win32;
using System.Collections;
using System.Collections.ObjectModel;
using System.Drawing;
using System.Dynamic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Xceed.Wpf.Toolkit;
using System;

namespace Sidequest
{
    public partial class MainWindow : Window
    {
        private static string dbPath;

        public ObservableCollection<Quest> listQuests { get; set; }
        public ObservableCollection<Quest> listFinishedQuests { get; set; }
        public ObservableCollection<Quest> listOverdueQuests { get; set; }
        public ObservableCollection<Quest> dayPlan { get; set; }

        public bool isAnimating = false;
        public bool canResize = false;

        private double _anchorRight;
        private double _anchorBottom;

        public static bool isMouseInside = false;

        public static string SaveFile = @"quests_savefile.txt";

        bool isCollapsed = true;
        bool isQuestEntryOpen = false;
        bool areSettingsOpen = false;
        Quest QuestToEdit = null;

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

                var pragmaCommand = connection.CreateCommand();
                pragmaCommand.CommandText = @"PRAGMA foreign_keys = ON;";
                pragmaCommand.ExecuteNonQuery();

                var command = connection.CreateCommand();
                command.CommandText = @"
                    CREATE TABLE IF NOT EXISTS Quests (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        QuestName TEXT NOT NULL,
                        Deadline TEXT,
                        Content TEXT,
                        Status BOOLEAN,
                        TimeEstimate TEXT
                    )";
                command.ExecuteNonQuery();

                var depCommand = connection.CreateCommand();
                depCommand.CommandText = @"
                    CREATE TABLE IF NOT EXISTS QuestDependencies (
                        QuestId INTEGER,
                        PrerequisiteId INTEGER,
                        PRIMARY KEY (QuestId, PrerequisiteId),
                        FOREIGN KEY (QuestId) REFERENCES Quests (Id) ON DELETE CASCADE,
                        FOREIGN KEY (PrerequisiteId) REFERENCES Quests (Id) ON DELETE CASCADE
                    )";
                depCommand.ExecuteNonQuery();
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

                command.CommandText = "SELECT Id, QuestName, Deadline, Content, Status, TimeEstimate FROM Quests ORDER BY Deadline ASC";

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
                        tempQuest.TimeEstimate = TimeOnly.Parse(reader.GetString(5));

                        sortedDeadlines.Add(tempQuest);
                    }
                }

                if (sortedDeadlines.Count == 0) return;
                
                while (sortedDeadlines.Count > 0)
                {
                    TimeSpan span = (sortedDeadlines[0].Deadline).Subtract(DateTime.Now);
                    if (span.TotalSeconds < 0 && !sortedDeadlines[0].IsCompleted)
                    {
                        Quest overdueQuest = null;
                        foreach (Quest quest in listQuests)
                        {
                            if (quest.ID == sortedDeadlines[0].ID) overdueQuest = quest;
                        }
                        if (overdueQuest != null) listQuests.Remove(overdueQuest);
                        listOverdueQuests.Add(overdueQuest);
                        
                        span = (sortedDeadlines[0].Deadline).Subtract(DateTime.Now);
                    }
                    else break;
                    sortedDeadlines.RemoveAt(0);
                }

                Console.WriteLine(sortedDeadlines.Count);

                if (sortedDeadlines.Count > 0)
                {
                    int i = 0;
                    while (i < sortedDeadlines.Count && sortedDeadlines[i].IsCompleted) i++;

                    if (i >= sortedDeadlines.Count)
                    {
                        MainBorder.BorderBrush = (SolidColorBrush)new BrushConverter().ConvertFrom("#2a2a2a");
                        return;
                    }

                    TimeSpan borderColorSpan = sortedDeadlines[i].Deadline.Subtract(DateTime.Now);

                    if (borderColorSpan.TotalHours <= 24 && borderColorSpan.TotalHours >= 0)
                    {
                        if (isCollapsed)
                        {
                            MainBorder.BorderBrush = new SolidColorBrush(Colors.Red);
                            return;
                        }
                    }

                    MainBorder.BorderBrush = (SolidColorBrush)new BrushConverter().ConvertFrom("#2a2a2a");
                }
                    
            }
        }

        private bool HasCycle(int node, Dictionary<int, List<int>> graph, HashSet<int> visited, HashSet<int> stack)
        {
            if (stack.Contains(node)) return true;
            if (visited.Contains(node)) return false;

            visited.Add(node);
            stack.Add(node);

            if (graph.ContainsKey(node))
            {
                foreach (var neighbor in graph[node])
                {
                    if (HasCycle(neighbor, graph, visited, stack))
                    {
                        return true;
                    }
                }
            }

            stack.Remove(node);
            return false;
        }

        private bool CheckForCycles(int questToEditId, List<int> newDependencies)
        {
            var QuestDAG = new Dictionary<int, List<int>>();

            using (var connection = new SqliteConnection(dbPath))
            {
                connection.Open();
                var command = connection.CreateCommand();

                command.CommandText = "SELECT QuestId, PrerequisiteId FROM QuestDependencies";
                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        int qId = reader.GetInt32(0);
                        int pId = reader.GetInt32(1);

                        if (!QuestDAG.ContainsKey(qId))
                        {
                            QuestDAG[qId] = new List<int>();
                            QuestDAG[qId].Add(pId);
                        }
                    }
                }
            }
            QuestDAG[questToEditId] = newDependencies;

            var visited = new HashSet<int>();
            var stack = new HashSet<int>();

            foreach (var node in QuestDAG.Keys)
            {
                if (HasCycle(node, QuestDAG, visited, stack))
                {
                    return true;
                }
            }

            return false;
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

                command.CommandText = "SELECT Id, QuestName, Deadline, Content, Status, TimeEstimate FROM Quests ORDER BY Deadline ASC";

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
                        loadedQuest.TimeEstimate = TimeOnly.Parse(reader.GetString(5));

                        if (!loadedQuest.IsCompleted) listQuests.Add(loadedQuest);
                        else listFinishedQuests.Add(loadedQuest);
                    }
                }
            }
        }

        

        private void ToggleNewQuestUI(object sender, RoutedEventArgs e)
        {
            if (!isQuestEntryOpen)
            {
                NewQuestEntryTextBox.Text = "";
                NewQuestContentTextBox.Text = "";

                QuestDependenciesComboBox.ItemsSource = listQuests;
                QuestDependenciesComboBox.SelectedItems.Clear();

                NewQuestEntry.Visibility = Visibility.Visible;
                isQuestEntryOpen = true;
            }
            else
            {
                NewQuestEntry.Visibility = Visibility.Collapsed;
                isQuestEntryOpen = false;
            }
                
        }

        private void ToggleEditQuestUI(object sender, RoutedEventArgs e)
        {
            if (!isQuestEntryOpen)
            {
                QuestToEdit = (sender as FrameworkElement).DataContext as Quest;
                if (QuestToEdit == null) return;

                var availableDependencies = new List<Quest>();
                foreach (var q in listQuests)
                {
                    if (q.ID != QuestToEdit.ID)
                    {
                        availableDependencies.Add(q);
                    }
                }

                QuestDependenciesComboBox.ItemsSource = availableDependencies;
                QuestDependenciesComboBox.SelectedItems.Clear();


                Quest CurrentQuest;
                NewQuestEntryTextBox.Text = QuestToEdit.QuestName;
                NewQuestContentTextBox.Text = QuestToEdit.QuestContents;
                NewQuestDeadlineDate.SelectedDate = QuestToEdit.Deadline.Date;

                NewQuestDeadlineTime.Text = QuestToEdit.Deadline.ToString("HH:mm");
                NewQuestTimeEstimate.Text = QuestToEdit.TimeEstimate.ToString("HH:mm");

                NewQuestEntry.Visibility = Visibility.Visible;
                isQuestEntryOpen = true;
            }
            else
            {
                NewQuestEntry.Visibility = Visibility.Collapsed;
                isQuestEntryOpen = false;
                QuestToEdit = null;
            }

        }

        private void SaveNewQuest(object sender, RoutedEventArgs e)
        {
            string newQuestName = NewQuestEntryTextBox.Text;
            if (string.IsNullOrWhiteSpace(newQuestName)) return;

            DateTime newQuestDeadline;

            if (NewQuestDeadlineDate.SelectedDate != null)
            {
                DateTime selectedDate = NewQuestDeadlineDate.SelectedDate.Value.Date;

                if (TimeSpan.TryParse(NewQuestDeadlineTime.Text, out TimeSpan selectedTime))
                {
                    newQuestDeadline = selectedDate.Add(selectedTime);
                }
                else
                {
                    System.Windows.MessageBox.Show("Invalid time format, use hh:mm format.");
                    return;
                }

                if (newQuestDeadline < DateTime.Now)
                {
                    System.Windows.MessageBox.Show("Invalid deadline, you can't schedule a quest in the past.");
                    return;
                }

                
            }
            else
            {
                newQuestDeadline = DateTime.Now.AddDays(1);
            }

            if (!TimeOnly.TryParse(NewQuestTimeEstimate.Text, out TimeOnly selectedTimeEstimate))
            {
                System.Windows.MessageBox.Show("Invalid time format, use hh:mm format.");
                return;
            }

            string newQuestContent = NewQuestContentTextBox.Text;

            Quest newQuest = new Quest();
            newQuest.QuestName = newQuestName;
            newQuest.Deadline = newQuestDeadline;
            newQuest.QuestContents = newQuestContent;
            newQuest.TimeEstimate = selectedTimeEstimate;
            listQuests.Add(newQuest);

            using (var connection = new SqliteConnection(dbPath))
            {
                connection.Open();

                var pragmaCommand = connection.CreateCommand();
                pragmaCommand.CommandText = @"PRAGMA foreign_keys = ON;";
                pragmaCommand.ExecuteNonQuery();

                var command = connection.CreateCommand();
                long currQuestId = 0;

                if (QuestToEdit == null) 
                {
                    command.CommandText = "INSERT INTO Quests (QuestName, Deadline, Content, Status, TimeEstimate) VALUES (@name, @date, @content, @status, @estimate); SELECT last_insert_rowid();";
                    command.Parameters.AddWithValue("@name", newQuestName);
                    command.Parameters.AddWithValue("@date", newQuestDeadline.ToString("s"));
                    command.Parameters.AddWithValue("@content", newQuestContent);
                    command.Parameters.AddWithValue("@status", false);
                    command.Parameters.AddWithValue("@estimate", selectedTimeEstimate);

                    currQuestId = (long)command.ExecuteScalar();
                    newQuest.ID = (int)currQuestId;
                }
                else
                {
                    List<int> newDependencies = new List<int>();

                    if (QuestDependenciesComboBox.SelectedItems != null)
                    {
                        foreach (var item in QuestDependenciesComboBox.SelectedItems)
                        {
                            if (item is Quest prereq) newDependencies.Add(prereq.ID);
                        }
                    }

                    if (CheckForCycles(QuestToEdit.ID, newDependencies))
                    {
                        System.Windows.MessageBox.Show("Invalid dependencies create a cycle, fix to continue.");
                        return;
                    }


                    command.CommandText = "UPDATE Quests SET QuestName = @name, Deadline = @date, Content = @content, TimeEstimate = @estimate WHERE Id = @id";
                    command.Parameters.AddWithValue("@id", QuestToEdit.ID);
                    command.Parameters.AddWithValue("@name", newQuestName);
                    command.Parameters.AddWithValue("@date", newQuestDeadline);
                    command.Parameters.AddWithValue("@content", newQuestContent);
                    command.Parameters.AddWithValue("@estimate", selectedTimeEstimate);

                    command.ExecuteNonQuery();
                    currQuestId = QuestToEdit.ID;

                    var deleteDepsCommand = connection.CreateCommand();
                    deleteDepsCommand.CommandText = @"DELETE FROM QuestDependencies WHERE QuestId = @id";
                    deleteDepsCommand.Parameters.AddWithValue("@id", currQuestId);
                    deleteDepsCommand.ExecuteNonQuery();
                }


                if (QuestDependenciesComboBox.SelectedItems != null)
                {
                    foreach (var item in QuestDependenciesComboBox.SelectedItems)
                    {
                        if (item is Quest prereqQuest)
                        {
                            var depCommand = connection.CreateCommand();
                            depCommand.CommandText = @"INSERT INTO QuestDependencies (QuestId, PrerequisiteId) VALUES (@qId, @pId)";
                            depCommand.Parameters.AddWithValue("@qId", currQuestId);
                            depCommand.Parameters.AddWithValue("@pId", prereqQuest.ID);
                            depCommand.ExecuteNonQuery();

                            if (QuestToEdit == null) newQuest.PrerequisitesIds.Add(prereqQuest.ID);
                        }
                    }
                }

            }

            QuestToEdit = null;
            NewQuestEntryTextBox.Text = "";
            NewQuestContentTextBox.Text = "";
            NewQuestDeadlineDate.SelectedDate = null;
            QuestDependenciesComboBox.SelectedItems.Clear();

            NewQuestEntry.Visibility = Visibility.Collapsed;
            isQuestEntryOpen = false;

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

            CheckDeadlines();
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
            animateWindow(400);   
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

            if (canResize && targetSize == 400) MainGrid.Visibility = Visibility.Visible;

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

        private void ToggleSettings(object sender, RoutedEventArgs e)
        {
            if (areSettingsOpen)
            {
                SettingsGrid.Visibility = Visibility.Collapsed;
                MainGrid.Visibility = Visibility.Visible;
                areSettingsOpen = false;
                return;
            }
            else
            {
                SettingsGrid.Visibility = Visibility.Visible;
                MainGrid.Visibility = Visibility.Collapsed;
                areSettingsOpen = true;
            }
            
        }


        public Dictionary<int, List<int>> BuildGraph()
        {
            var graph = new Dictionary<int, List<int>>();

            foreach (var quest in listQuests)
            {
                graph[quest.ID] = new List<int>();
            }

            using (var connection = new SqliteConnection(dbPath))
            {
                connection.Open();
                var command = connection.CreateCommand();
                command.CommandText = "SELECT PrerequisiteId, QuestId FROM QuestDependencies";

                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        int pId = reader.GetInt32(0);
                        int qId = reader.GetInt32(1);

                        if (graph.ContainsKey(pId))
                        {
                            graph[pId].Add(qId);
                        }
                    }
                }
            }

            return graph;
        }

        private int EvaluateQuestUrgency(Quest quest, Dictionary<int, List<int>> graph)
        {
            double timeLeft = (quest.Deadline - DateTime.Now).TotalHours;
            double timeReserve = timeLeft - (quest.TimeEstimate.Hour + (quest.TimeEstimate.Minute / 60.0));

            double timeScore = 1000 / Math.Max(timeReserve, 0.1);

            int neighbors = 0;
            if (graph.ContainsKey(quest.ID))
            {
                neighbors = graph[quest.ID].Count;
            }

            int blockedQuestsScore = neighbors * 50;

            return (int) (timeScore + blockedQuestsScore);
        }

        private ObservableCollection<Quest> PlanQuests(ObservableCollection<Quest> quests, Dictionary<int, List<int>> graph)
        {
            Dictionary<int, Quest> questLookup = new Dictionary<int, Quest>();

            var comparer = Comparer<int>.Create((a, b) => b.CompareTo(a));
            PriorityQueue<Quest, int> questHeap = new PriorityQueue<Quest, int>(comparer);

            Dictionary<int, int> inDegree = new Dictionary<int, int>();

            foreach (Quest q in quests)
            {
                questLookup[q.ID] = q;
                inDegree[q.ID] = 0;
            }

            foreach (var kvp in graph)
            {
                foreach (int neighborId in kvp.Value)
                {
                    if (inDegree.ContainsKey(neighborId))
                    {
                        inDegree[neighborId]++;
                    }
                }
            }

            foreach (Quest q in quests)
            {
                if (inDegree[q.ID] == 0)
                {
                    questHeap.Enqueue(q, EvaluateQuestUrgency(q, graph));
                }
            }

            ObservableCollection<Quest> finalPlan = new ObservableCollection<Quest>();

            while (questHeap.Count > 0)
            {
                Quest currQuest = questHeap.Dequeue();
                finalPlan.Add(currQuest);

                if (graph.ContainsKey(currQuest.ID))
                {
                    foreach (int neighborId in graph[currQuest.ID])
                    {
                        inDegree[neighborId]--;

                        if (inDegree[neighborId] == 0)
                        {
                            Quest unlockedQuest = questLookup[neighborId];
                            questHeap.Enqueue(unlockedQuest, EvaluateQuestUrgency(unlockedQuest, graph));
                        }
                    }
                }
            }

            return finalPlan;
        }


        private void StartPlanner(object sender, RoutedEventArgs e)
        {
            Dictionary<int, List<int>> graph = BuildGraph();

            ObservableCollection<Quest> newPlan = PlanQuests(listQuests, graph);
            dayPlan.Clear();

            foreach (Quest q in newPlan)
            {
                dayPlan.Add(q);
            }
        }


        private void QuitApp(object sener, RoutedEventArgs e)
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
                dayPlan = new ObservableCollection<Quest>();
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