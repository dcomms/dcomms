
using Dcomms.P2PTP.Extensibility;
using Dcomms.P2PTP.LocalLogic;
using Dcomms.SUBT;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq.Expressions;
using System.Net;
using System.Reflection;
using System.Text;
using System.Timers;
using System.Windows.Input;
using Xamarin.Forms;

namespace StarTrinity.CST
{
    public class MainViewModel : BaseNotify
    {
        ILocalPeer _localPeer;
        SubtLocalPeer _subtLocalPeer;
        Timer _timer;

        class User : ILocalPeerUser
        {
            bool ILocalPeerUser.EnableLog => true;
            void ILocalPeerUser.WriteToLog(string message)
            {
                //   Console.WriteLine(message);
            }
        }

        SubtMeasurement _latestMeasurement;
        public SubtMeasurement LatestMeasurement => _latestMeasurement;
      
        public bool StartEnabled { get; set; } = true;
        public ICommand Start => new DelegateCommand(() =>
        {
            if (_timer == null)
            {
                var coordinatorServerIp1 = IPAddress.Parse("163.172.210.13");//neth3
                var coordinatorServerIp2 = IPAddress.Parse("195.154.173.208");//fra2
                _subtLocalPeer = new SubtLocalPeer(new SubtLocalPeerConfiguration
                {
                    SenderThreadsCount = 3,
                    BandwidthTarget = 1024 * 100
                });
                _localPeer = new LocalPeer(new LocalPeerConfiguration
                {
                    RoleAsUser = true,
                    LocalPeerUser = new User(),
                    LocalUdpPortRangeStart = null,
                    SocketsCount = 3,
                    Coordinators = new IPEndPoint[]
                    {
                    new IPEndPoint(coordinatorServerIp1, 10000),
                    new IPEndPoint(coordinatorServerIp1, 10001),
                    new IPEndPoint(coordinatorServerIp1, 10002),
                  //  new IPEndPoint(coordinatorServerIp1, 10003),
                 //   new IPEndPoint(coordinatorServerIp1, 10004),
                 //   new IPEndPoint(coordinatorServerIp1, 10005),
                  //  new IPEndPoint(coordinatorServerIp1, 10006),
                  //  new IPEndPoint(coordinatorServerIp1, 10007),
                    new IPEndPoint(coordinatorServerIp1, 9000),
                    new IPEndPoint(coordinatorServerIp1, 9001),
                    new IPEndPoint(coordinatorServerIp1, 9002),
                 //   new IPEndPoint(coordinatorServerIp1, 9003),
                    new IPEndPoint(coordinatorServerIp2, 9000),
                    new IPEndPoint(coordinatorServerIp2, 9001),
                    new IPEndPoint(coordinatorServerIp2, 9002),
                 //   new IPEndPoint(coordinatorServerIp2, 9003),
                    },
                    Extensions = new[]
                    {
                        _subtLocalPeer
                    }
                });
                _subtLocalPeer.MeasurementsHistory.OnAddedNewMeasurement += MeasurementsHistory_OnAddedNewMeasurement;

                _timer = new Timer(100);
                _timer.Elapsed += _timer_Elapsed;
                _timer.Start();

                StartEnabled = false;
                RaisePropertyChanged(() => StartEnabled);
            }
        });

        private void _timer_Elapsed(object sender, ElapsedEventArgs e)
        {
            if (_localPeer != null)
            {
                _localPeer.InvokeInManagerThread(() =>
                {
                    if (_subtLocalPeer != null)
                    {
                        _latestMeasurement = _subtLocalPeer.MeasurementsHistory.Measure(_subtLocalPeer);
                        Device.BeginInvokeOnMainThread(() =>
                        {
                            RaisePropertyChanged(() => LatestMeasurement);
                        });
                    }
                });
            }

            throw new NotImplementedException();
        }

        private void MeasurementsHistory_OnAddedNewMeasurement(SubtMeasurement subtMeasurement)
        {
            Device.BeginInvokeOnMainThread(() =>
            {
                _subtMeasurements.Insert(0, subtMeasurement);
            });
        }

        ObservableCollection<SubtMeasurement> _subtMeasurements = new ObservableCollection<SubtMeasurement>();
        public ObservableCollection<SubtMeasurement> SubtMeasurements => _subtMeasurements;
    }



    public class DelegateCommand : ICommand
    {
        readonly Action action;
        public DelegateCommand(Action action)
        {
            if (action == null) throw new ArgumentNullException();
            this.action = action;
        }
        public event EventHandler CanExecuteChanged;
        public bool CanExecute(object parameter)
        {
            return true;
        }
        public void Execute(object parameter)
        {
            action();
        }
    }
    public abstract class BaseNotify : INotifyPropertyChanged
    {
        /// <summary>
        /// Raised when a property on this object has a new value.
        /// </summary>
        public event PropertyChangedEventHandler PropertyChanged;



        /// <summary>
        /// Raises this object's PropertyChanged event.
        /// </summary>
        /// <param name="propertyName">The property that has a new value.</param>
        public virtual void RaisePropertyChanged(string propertyName)
        {
            var handler = PropertyChanged;
            if (handler != null)
            {
                handler(this, new PropertyChangedEventArgs(propertyName));
            }
        }


        /// <summary>
        /// Raises this object's PropertyChanged event.
        /// </summary>
        /// <typeparam name="T">The type of the property that has a new value</typeparam>
        /// <param name="propertyExpression">A Lambda expression representing the property that has a new value.</param>
        public void RaisePropertyChanged<T>(Expression<Func<T>> propertyExpression)
        {
            var propertyName = ExtractPropertyName(propertyExpression);
            RaisePropertyChanged(propertyName);
        }

        protected static string ExtractPropertyName<T>(Expression<Func<T>> propertyExpression)
        {
            if (propertyExpression == null)
            {
                throw new ArgumentNullException("propertyExpression");
            }

            var memberExpression = propertyExpression.Body as MemberExpression;
            if (memberExpression == null)
            {
                throw new ArgumentException();
            }

            var property = memberExpression.Member as PropertyInfo;
            if (property == null)
            {
                throw new ArgumentException();
            }

            var getMethod = property.GetGetMethod(true);

            if (getMethod == null)
            {
                // this shouldn't happen - the expression would reject the property before reaching this far
                throw new ArgumentException();
            }

            if (getMethod.IsStatic)
            {
                throw new ArgumentException();
            }

            return memberExpression.Member.Name;
        }
    }
}
