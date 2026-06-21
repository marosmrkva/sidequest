using System;
using System.Collections.Generic;
using System.Text;


namespace Sidequest
{
    public class Quest
    {
        public int ID { get; set; }
        public string QuestName { get; set; }
        public string QuestContents { get; set; }

        public bool IsCompleted = false;
        public DateTime Deadline { get; set; }

        public TimeOnly TimeEstimate { get; set; }

    }
}
