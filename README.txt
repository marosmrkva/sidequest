Sidequest - Intelligent Task Planner
====================================

Sidequest je task manager s integrovanym inteligentnym planovacom. Pomocou teorie grafov (DAG) a topologickeho triedenia dokaze z uloh s roznymi terminmi a vzajomnymi zavislostami vygenerovat optimalny denny rozvrh vratane dynamicky rozlozenych prestavok.

Pouzite technologie a algoritmy:
--------------------------------
- C# / WPF / .NET
- SQLite (lokalna databaza s relaciami pre zavislosti)
- Orientovane acyklicke grafy (DAG) pre modelovanie zavislosti uloh
- Modifikovany Kahnov algoritmus a prioritna fronta (Max-Heap) pre generovanie planu
- Prehladavanie do hlbky (DFS) na detekciu cyklov v zavislostiach


Spustenie (Quick Start):
------------------------
Aplikacia je kompilovana ako Self-Contained Single File. Nevyzaduje instalaciu .NET Runtime.

1. Otvorte zlozku s aplikaciou.
2. Spustite subor Sidequest.exe.
3. Databaza quests.db sa automaticky vygeneruje pri prvom spusteni v rovnakom priecinku.


Kompilacia zo zdrojovych kodov (pre hodnotiacich):
--------------------------------------------------
1. Otvorte Sidequest.sln vo Visual Studiu.
2. Nastavte Sidequest ako Startup projekt a zvolte Build / Start.


Kde najst viac informacii:
--------------------------
- Detailny popis architektury a matematickeho modelu algoritmov najdete v subore documentation.pdf
- Navod na pouzivanie a planovanie je v subore user_documentation.pdf
