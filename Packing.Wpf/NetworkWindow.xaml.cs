using System;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using Packing.Network;

namespace Packing.Wpf {
    public partial class NetworkWindow : Window {
        private readonly ServerRegistry workerDirectory;
        private readonly DispatcherTimer refreshClock;
        private readonly CancellationTokenSource windowStop = new CancellationTokenSource();
        private bool isRefreshing;
        public NetworkWindow(ServerRegistry workerDirectory) {
            InitializeComponent();
            this.workerDirectory = workerDirectory;
            refreshClock = new DispatcherTimer {
                Interval = TimeSpan.FromSeconds(2)
            };
            refreshClock.Tick += Refresh;
            refreshClock.Start();
            Refresh(null, EventArgs.Empty);
        }

        private async void Refresh(object eventSource, EventArgs eventData) {
            if (isRefreshing) {
                return;
            }

            isRefreshing = true;
            try {
                await workerDirectory.ProbeAsync(windowStop.Token);
                if (windowStop.IsCancellationRequested) {
                    return;
                }

                string selectedEndpoint = (WorkerTable.SelectedItem as WorkerSnapshot)?.Address;
                WorkerSnapshot[] currentSnapshot = workerDirectory.Snapshot();
                WorkerTable.ItemsSource = currentSnapshot;
                WorkerTable.SelectedItem = currentSnapshot.FirstOrDefault(entry => entry.Address == selectedEndpoint);
                ActivityTable.ItemsSource = workerDirectory.Events();
            } catch (Exception caughtError) {
                if (!windowStop.IsCancellationRequested) {
                    ConnectionLabel.Text = caughtError.Message;
                }
            } finally {
                isRefreshing = false;
            }
        }

        private void OnAdd(object eventSource, RoutedEventArgs eventData) {
            try {
                workerDirectory.Add(EndpointInput.Text);
                WorkerTable.ItemsSource = workerDirectory.Snapshot();
                ConnectionLabel.Text = "Сервер добавлен.";
                Refresh(null, EventArgs.Empty);
            } catch (ArgumentException caughtError) {
                ConnectionLabel.Text = caughtError.Message;
            }
        }

        private void OnRemove(object eventSource, RoutedEventArgs eventData) {
            WorkerSnapshot entry = WorkerTable.SelectedItem as WorkerSnapshot;
            if (entry == null) {
                return;
            }

            workerDirectory.Remove(entry.Address);
            WorkerTable.ItemsSource = workerDirectory.Snapshot();
        }

        private void OnToggle(object eventSource, RoutedEventArgs eventData) {
            WorkerSnapshot entry = WorkerTable.SelectedItem as WorkerSnapshot;
            if (entry == null) {
                return;
            }

            workerDirectory.SetEnabled(entry.Address, !entry.Enabled);
            WorkerTable.ItemsSource = workerDirectory.Snapshot();
        }

        private void OnClosing(object eventSource, CancelEventArgs eventData) {
            refreshClock.Stop();
            windowStop.Cancel();
        }
    }
}
