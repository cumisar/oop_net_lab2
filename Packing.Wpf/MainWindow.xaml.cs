using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Packing.Core;
using Packing.Network;

namespace Packing.Wpf {
    public partial class MainWindow : Window {
        private CancellationTokenSource searchStop;
        private int searchSequence;
        private bool closeWhenIdle;
        private readonly ServerRegistry computeWorkers = new ServerRegistry();
        private NetworkWindow workerDialog;
        public MainWindow() {
            InitializeComponent();
            ParallelLimitInput.Text = Math.Min(256, Environment.ProcessorCount).ToString(CultureInfo.InvariantCulture);
            int highestValue = Math.Min(256, Environment.ProcessorCount);
            var parallelismSet = new SortedSet<int> {
                1,
                highestValue
            };
            for (int amount = 2; amount <= highestValue; amount *= 2) {
                parallelismSet.Add(amount);
            }

            ComparisonInput.Text = string.Join(", ", parallelismSet);
            Closed += (eventSource, eventData) => {
                workerDialog?.Close();
                computeWorkers.Dispose();
            };
        }

        private int ReadInteger(TextBox inputBox, string inputLabel, int lowerLimit, int upperLimit) {
            int parsedValue;
            if (!int.TryParse(inputBox.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out parsedValue) || parsedValue < lowerLimit || parsedValue > upperLimit) {
                throw new ArgumentException(inputLabel + ": введите целое число от " + lowerLimit + " до " + upperLimit + ".");
            }

            return parsedValue;
        }

        private void AddFigures(List<Figure> tiles, TextBox inputBox, int horizontalSize, int verticalSize) {
            int amount = ReadInteger(inputBox, "Количество " + horizontalSize + "×" + verticalSize, 0, PackingProblem.FigureLimit);
            for (int cursorIndex = 0; cursorIndex < amount; cursorIndex++) {
                tiles.Add(new Figure(horizontalSize, verticalSize));
            }
        }

        private void ReadSearchInputs(out PackingProblem packingTask, out GeneticSettings searchOptions) {
            var tiles = new List<Figure>();
            AddFigures(tiles, UnitSquares, 1, 1);
            AddFigures(tiles, DoubleSquares, 2, 2);
            AddFigures(tiles, TripleSquares, 3, 3);
            if (UseOblongTiles.IsChecked == true) {
                AddFigures(tiles, TallDominoes, 1, 2);
                AddFigures(tiles, WideDominoes, 2, 1);
                AddFigures(tiles, TallTriples, 1, 3);
                AddFigures(tiles, WideTriples, 3, 1);
            }

            packingTask = new PackingProblem(tiles);
            double mutationChance;
            if (!double.TryParse(VariationInput.Text.Trim().Replace(',', '.'), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out mutationChance)) {
                throw new ArgumentException("Мутация: введите число от 0 до 1 (например, 0,25).");
            }

            searchOptions = new GeneticSettings {
                PopulationSize = ReadInteger(PoolSizeInput, "Популяция", 8, 2000),
                MaxGenerations = ReadInteger(EpochLimitInput, "Поколения", 1, 1000000),
                EliteCount = 4,
                MutationProbability = mutationChance,
                ThreadCount = ReadInteger(ParallelLimitInput, "Потоки", 1, 256)
            };
            searchOptions.Validate();
        }

        private async void OnStart(object eventSource, RoutedEventArgs eventData) {
            if (searchStop != null) {
                return;
            }

            PackingProblem packingTask;
            GeneticSettings searchOptions;
            try {
                ReadSearchInputs(out packingTask, out searchOptions);
                if (UseWorkers.IsChecked == true && !computeWorkers.Snapshot().Any(entry => entry.Enabled)) {
                    throw new ArgumentException("Добавьте сервер в окне мониторинга.");
                }
            } catch (ArgumentException caughtError) {
                IssueLabel.Text = caughtError.Message;
                return;
            }

            IssueLabel.Text = "";
            TaskInputs.IsEnabled = false;
            RunSearch.IsEnabled = false;
            HaltSearch.IsEnabled = true;
            MeasureThreads.IsEnabled = false;
            UseWorkers.IsEnabled = false;
            TimingCanvas.Visibility = Visibility.Collapsed;
            LayoutCanvas.Solution = null;
            ResultLabel.Text = "Подготовка популяции…";
            IterationLabel.Text = "";
            ActivityLabel.Text = "Поиск…";
            searchStop = new CancellationTokenSource();
            CancellationTokenSource activeStop = searchStop;
            int searchStamp = ++searchSequence;
            var progressSink = new Progress<SearchProgress>(reportedStep => {
                if (searchStamp == searchSequence && searchStop == activeStop) {
                    ShowProgress(reportedStep);
                }
            });
            using DistributedEvaluator layoutExecutor = UseWorkers.IsChecked == true ? new DistributedEvaluator(computeWorkers) : null;
            try {
                SearchResult searchOutcome = await Task.Run(() => new GeneticSolver().Solve(packingTask, searchOptions, progressSink, activeStop.Token, layoutExecutor));
                ShowProgress(searchOutcome.Progress);
                if (searchOutcome.Reason == StopReason.AreaLowerBound) {
                    ActivityLabel.Text = "Найден глобальный минимум.";
                } else if (searchOutcome.Reason == StopReason.Cancelled) {
                    ActivityLabel.Text = "Поиск остановлен · лучшее найденное.";
                } else {
                    ActivityLabel.Text = "Лимит поколений · лучшее найденное.";
                }

                if (layoutExecutor != null) {
                    ActivityLabel.Text += " Сеть: " + layoutExecutor.RemoteCandidates + ", локально: " + layoutExecutor.LocalCandidates + ".";
                }
            } catch (Exception caughtError) {
                IssueLabel.Text = "Не удалось завершить поиск: " + caughtError.Message;
                ActivityLabel.Text = "Ошибка поиска.";
            } finally {
                searchStop = null;
                activeStop.Dispose();
                TaskInputs.IsEnabled = true;
                RunSearch.IsEnabled = true;
                HaltSearch.IsEnabled = false;
                MeasureThreads.IsEnabled = true;
                UseWorkers.IsEnabled = true;
                if (closeWhenIdle) {
                    Close();
                }
            }
        }

        private void ShowProgress(SearchProgress progressSink) {
            PackingSolution bestLayout = progressSink.Best;
            LayoutCanvas.Solution = bestLayout;
            ResultLabel.Text = string.Format(CultureInfo.GetCultureInfo("ru-RU"), "S = {0}  ·  {1} × {2}", bestLayout.Area, bestLayout.Width, bestLayout.Height);
            IterationLabel.Text = string.Format(CultureInfo.GetCultureInfo("ru-RU"), "Поколение {0}  ·  {1:F2} с", progressSink.Generation, progressSink.Elapsed.TotalSeconds);
        }

        private int[] ReadProfileThreads() {
            var parallelismSet = new SortedSet<int> {
                1
            };
            string[] numberParts = ComparisonInput.Text.Split(new[] { ',', ';', ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            if (numberParts.Length == 0) {
                throw new ArgumentException("Укажите число потоков для сравнения.");
            }

            foreach (string numberPart in numberParts) {
                int parsedValue;
                if (!int.TryParse(numberPart, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsedValue) || parsedValue < 1 || parsedValue > 256) {
                    throw new ArgumentException("Потоки для сравнения: целые числа от 1 до 256.");
                }

                parallelismSet.Add(parsedValue);
            }

            if (parallelismSet.Count > 12) {
                throw new ArgumentException("Для сравнения можно указать до 12 значений.");
            }

            return parallelismSet.ToArray();
        }

        private async void OnProfile(object eventSource, RoutedEventArgs eventData) {
            if (searchStop != null) {
                return;
            }

            PackingProblem packingTask;
            GeneticSettings searchOptions;
            int[] parallelismSet;
            try {
                ReadSearchInputs(out packingTask, out searchOptions);
                parallelismSet = ReadProfileThreads();
            } catch (ArgumentException caughtError) {
                IssueLabel.Text = caughtError.Message;
                return;
            }

            IssueLabel.Text = "";
            TaskInputs.IsEnabled = false;
            RunSearch.IsEnabled = false;
            HaltSearch.IsEnabled = true;
            MeasureThreads.IsEnabled = false;
            TimingCanvas.Clear();
            TimingCanvas.Visibility = Visibility.Visible;
            ActivityLabel.Text = "Сравнение локальных потоков…";
            searchStop = new CancellationTokenSource();
            CancellationTokenSource activeStop = searchStop;
            int searchStamp = ++searchSequence;
            var progressSink = new Progress<ThreadMeasurement>(timingSample => {
                if (searchStamp == searchSequence && searchStop == activeStop) {
                    TimingCanvas.Add(timingSample);
                    ActivityLabel.Text = string.Format(CultureInfo.GetCultureInfo("ru-RU"), "Потоки: {0} · {1:F1} мс", timingSample.ThreadCount, timingSample.Milliseconds);
                }
            });
            try {
                IReadOnlyList<ThreadMeasurement> timingRuns = await Task.Run(() => new ThreadProfiler().Run(packingTask, searchOptions, parallelismSet, progressSink, activeStop.Token));
                foreach (ThreadMeasurement timingSample in timingRuns) {
                    TimingCanvas.Add(timingSample);
                }

                ThreadMeasurement quickestRun = timingRuns.OrderBy(entry => entry.Milliseconds).First();
                ActivityLabel.Text = timingRuns.All(entry => entry.Evaluations <= 1) ? "Минимум найден до распараллеливания." : string.Format(CultureInfo.GetCultureInfo("ru-RU"), "Лучшее время: {0:F1} мс · потоки: {1}", quickestRun.Milliseconds, quickestRun.ThreadCount);
            } catch (OperationCanceledException) {
                ActivityLabel.Text = "Сравнение остановлено.";
            } catch (Exception caughtError) {
                IssueLabel.Text = "Не удалось выполнить сравнение: " + caughtError.Message;
                ActivityLabel.Text = "Ошибка сравнения.";
            } finally {
                searchStop = null;
                activeStop.Dispose();
                TaskInputs.IsEnabled = true;
                RunSearch.IsEnabled = true;
                HaltSearch.IsEnabled = false;
                MeasureThreads.IsEnabled = true;
                UseWorkers.IsEnabled = true;
                if (closeWhenIdle) {
                    Close();
                }
            }
        }

        private void OnNetwork(object eventSource, RoutedEventArgs eventData) {
            if (workerDialog == null) {
                workerDialog = new NetworkWindow(computeWorkers) {
                    Owner = this
                };
                workerDialog.Closed += (closedSender, launchArguments) => {
                    workerDialog = null;
                };
                workerDialog.Show();
            } else {
                workerDialog.Activate();
            }
        }

        private void OnStop(object eventSource, RoutedEventArgs eventData) {
            searchStop?.Cancel();
            HaltSearch.IsEnabled = false;
            ActivityLabel.Text = "Остановка…";
        }

        private void OnClosing(object eventSource, CancelEventArgs eventData) {
            if (searchStop != null) {
                eventData.Cancel = true;
                closeWhenIdle = true;
                searchStop.Cancel();
            }
        }

        private void OnRectangleModeChanged(object eventSource, RoutedEventArgs eventData) {
            if (OblongInputs != null) {
                OblongInputs.Visibility = UseOblongTiles.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            }
        }
    }
}
