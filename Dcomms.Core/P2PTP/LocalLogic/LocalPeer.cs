using Dcomms.P2PTP.Extensibility;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Dcomms.P2PTP.LocalLogic
{
    /// <summary>
    /// main class used by application-level (user)
    /// creates and holds instances of receiver(s), nodeUser, manager, sender, license server
    /// </summary>
    public partial class LocalPeer: IDisposable, ILocalPeer
    {
        readonly DateTime _startTimeUtc = DateTime.UtcNow;
        readonly Stopwatch _stopwatch = Stopwatch.StartNew();
        public DateTime DateTimeNowUtc { get { return _startTimeUtc + _stopwatch.Elapsed; } }
        /// <summary>
        /// 32-bit timestamp used in requests; comes from TimeSpan.Ticks; is looped every 429 seconds
        /// </summary>
        internal uint Time32 {  get { return (uint)((ulong)_stopwatch.Elapsed.Ticks & 0x00000000FFFFFFFF); } }

        IConnectedPeer[] ILocalPeer.ConnectedPeers => Manager?.ConnectedPeers;
        uint ILocalPeer.Time32 => Time32;
        long ILocalPeer.Time64 => _stopwatch.Elapsed.Ticks;
        Random ILocalPeer.Random => Random;
        DateTime ILocalPeer.DateTimeNowUtc => DateTimeNowUtc;

        internal List<SocketWithReceiver> Receivers; // list is not modified after startup // is read by many threads
        public IEnumerable<SocketWithReceiver> SocketWithReceivers => Receivers?.ToList(); // need to copy list here to make WPF GUI updated
        internal Manager Manager; // set by Manager in ctor, intentionally
        public PeerId LocalPeerId { get; private set; } = new PeerId(Guid.NewGuid());
        PeerId ILocalPeer.LocalPeerId => LocalPeerId;

        LocalPeerConfiguration ILocalPeer.Configuration => Configuration;
        internal readonly LocalPeerConfiguration Configuration;
        internal readonly Dictionary<string, ILocalPeerExtension> ExtensionsById;
        internal readonly ILocalPeerUser User;
        internal readonly Random Random = new Random();
        internal readonly Firewall Firewall = new Firewall();
        internal readonly SysAdminFeedbackChannel SysAdminFeedbackChannel = new SysAdminFeedbackChannel();
        static LocalPeer _instance;
        public LocalPeer(LocalPeerConfiguration configuration)
        {
            if (_instance != null) throw new InvalidOperationException();
            _instance = this;
            if (configuration.LocalPeerUser == null) throw new ArgumentNullException(nameof(configuration.LocalPeerUser));
            if (configuration.Extensions == null) configuration.Extensions = new ILocalPeerExtension[0];
            User = configuration.LocalPeerUser;
            Configuration = configuration;
            if (configuration.RoleAsUser)
            { // client
                if (configuration.RoleAsSharedPassive || configuration.RoleAsCoordinator) throw new ArgumentException(nameof(configuration.RoleAsUser));
                if (configuration.Coordinators == null || configuration.Coordinators.Length < 1) throw new ArgumentException(nameof(configuration.Coordinators));
                //  if (configuration.SubtUserTargetBandwidthBps == null) throw new ArgumentException(nameof(configuration.SubtUserTargetBandwidthBps));
            }
            else if (configuration.RoleAsCoordinator)
            { // server
                if (configuration.LocalUdpPortRangeStart == null) throw new ArgumentException(nameof(configuration.LocalUdpPortRangeStart));
            }
            else if (configuration.RoleAsSharedPassive)
            {

            }
            else throw new Exception("no roles");

            if (configuration.SocketsCount <= 0 || configuration.SocketsCount > 2000) throw new ArgumentException(nameof(configuration.SocketsCount));

            ExtensionsById = configuration.Extensions.ToDictionary(ext => ext.ExtensionId, ext => ext);
            Initialize();
        }
        void Initialize() // can be called twice, after previous disposing
        {
            if (Receivers != null) throw new InvalidOperationException();

            // open udp socket(s)
            Receivers = new List<SocketWithReceiver>(Configuration.SocketsCount);
            for (int socketIndex = 0; socketIndex < Configuration.SocketsCount; socketIndex++)
            {
                var socket = new UdpClient(Configuration.LocalUdpPortRangeStart.HasValue ? (Configuration.LocalUdpPortRangeStart.Value + socketIndex) : 0);
                Receivers.Add(new SocketWithReceiver(this, socket));
            }
                        
            foreach (var extension in ExtensionsById.Values)
                extension.ReinitializeWithLocalPeer(this);

            if (Manager != null) throw new InvalidOperationException();
            new Manager(this);
        }
        internal void HandleException(string module, Exception exc)
        {
            WriteToLog(module, "error: " + exc);
        }
        public void HandleGuiException( Exception exc)
        {
            WriteToLog(LogModules.Gui, "error: " + exc);
        }
        public void Dispose()
        {
            Dispose(false);
        }
        public void Dispose(bool currentManagerWillDisposeItselfAfterThisProcedure)
        {
            foreach (var extension in ExtensionsById.Values)
                extension.DestroyWithLocalPeer();
            if (!currentManagerWillDisposeItselfAfterThisProcedure) Manager.Dispose();
            Manager = null;            
            foreach (var receiver in Receivers)
                receiver.Dispose();
            Receivers = null;

            _instance = null;
        }
        internal void WriteToLog(string module, string message)
        {
            if (User.EnableLog)
                User.WriteToLog($"[{module}] {message}");
        }
       
        public void ReinitializeByGui()
        {
            Manager.InvokeInManagerThread(Manager.Reinitialize);
        }
       
        /// <summary>
        /// must be called by [current and old] manager only, it disposes itself after this procedure. new manager is created inside this procedure
        /// </summary>
        internal void Reinitialize_CalledByManagerOnly()
        {           
            try
            {
                WriteToLog(LogModules.GeneralManager, "reinitializing...");     
                Dispose(true);
                Initialize();
            }
            catch (Exception exc)
            {
                HandleException(LogModules.GeneralManager, exc);
            }          
        }

        void ILocalPeer.HandleException(ILocalPeerExtension extension, Exception exception)
        {
            HandleException(extension.ExtensionId, exception);
        }
        void ILocalPeer.WriteToLog(ILocalPeerExtension extension, string message)
        {
            WriteToLog(extension.ExtensionId, message);
        }
        void ILocalPeer.InvokeInManagerThread(Action a) => Manager?.InvokeInManagerThread(a);
    }

    internal static class LogModules
    {
        internal static string Receiver = "receiver";
        internal static string GeneralManager = "manager";
        internal static string Hello = "hello";
        internal static string PeerSharing = "peerSharing";
        internal static string Gui = "gui";
    }
}
