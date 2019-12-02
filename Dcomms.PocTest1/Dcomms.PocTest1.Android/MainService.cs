using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using Android.App;
using Android.Content;
using Android.OS;
using Android.Runtime;
using Android.Views;
using Android.Widget;

namespace Dcomms.PocTest1.Droid
{
    [Service]
    public class MainService : Service
    {
        public const string ACTION_START_SERVICE = "ACTION_START_SERVICE";
        const int SERVICE_RUNNING_NOTIFICATION_ID = 10000;
        const string ACTION_MAIN_ACTIVITY = "ACTION_MAIN_ACTIVITY";
        const string CHANNEL_ID = "CHANNEL_ID";

        static bool NewAndroidApiVersion => Build.VERSION.SdkInt >= BuildVersionCodes.O;
        static List<Action<Poc1Model>> _startServiceCallbacks = new List<Action<Poc1Model>>();
        public static void StartService(Context context, Action<Poc1Model> cb = null)
        {

            if (_poc1 != null) cb?.Invoke(_poc1);
            else
            {
                var startServiceIntent = new Intent(context, typeof(MainService));
                startServiceIntent.SetAction(MainService.ACTION_START_SERVICE);
                
                if (NewAndroidApiVersion) context.StartForegroundService(startServiceIntent);
                else context.StartService(startServiceIntent);

                if (cb != null) _startServiceCallbacks.Add(cb);
            }
        }

        static Poc1Model _poc1;
        public override void OnCreate()
        {
            base.OnCreate();
           // Log.Info(TAG, "OnCreate: the service is initializing.");

         //   _poc1 = new Poc1Model();
          //  handler = new Handler();

            // This Action is only for demonstration purposes.
            //runnable = new Action(() =>
            //{
            //    if (timestamper == null)
            //    {
            //        Log.Wtf(TAG, "Why isn't there a Timestamper initialized?");
            //    }
            //    else
            //    {
            //        string msg = timestamper.GetFormattedTimestamp();
            //        Log.Debug(TAG, msg);
            //        Intent i = new Intent(Constants.NOTIFICATION_BROADCAST_ACTION);
            //        i.PutExtra(Constants.BROADCAST_MESSAGE_KEY, msg);
            //        Android.Support.V4.Content.LocalBroadcastManager.GetInstance(this).SendBroadcast(i);
            //        handler.PostDelayed(runnable, Constants.DELAY_BETWEEN_LOG_MESSAGES);
            //    }
            //});
        }

     

        public override StartCommandResult OnStartCommand(Intent intent, StartCommandFlags flags, int startId)
        {
            if (intent.Action.Equals(ACTION_START_SERVICE))
            {
                if (_poc1 != null)
                {
                  //  Log.Info(TAG, "OnStartCommand: The service is already running.");
                }
                else
                {
                    //  Log.Info(TAG, "OnStartCommand: The service is starting.");
                    //   RegisterForegroundService();
                    //   handler.PostDelayed(runnable, Constants.DELAY_BETWEEN_LOG_MESSAGES);
                    //   isStarted = true;

                    _poc1 = new Poc1Model(true);
                    _poc1.SevereError += (errorMsg) =>
                    {
                        Android.Util.Log.Error("Dcomms", errorMsg);
                    };

                    if (NewAndroidApiVersion)
                    {
                        var channel = new NotificationChannel(CHANNEL_ID, "dcomms poc1", NotificationImportance.Default)
                        {
                            Description = "dcomms poc1"
                        };
                        var notificationManager = (NotificationManager)GetSystemService(NotificationService);
                        notificationManager.CreateNotificationChannel(channel);
                    }
                   
                    var notification = (NewAndroidApiVersion ? new Notification.Builder(this, CHANNEL_ID) : new Notification.Builder(this))
                        .SetContentTitle("Dcomms PoC1")
                        .SetContentText("Dcomms PoC1")
                        .SetSmallIcon(Resource.Drawable.xamarin_logo// ic_stat_name
                        )
                        .SetContentIntent(BuildIntentToShowMainActivity())
                        .SetOngoing(true)
                    //    .AddAction(BuildRestartTimerAction())
                    //    .AddAction(BuildStopServiceAction())
                        .Build();



                    // Enlist this instance of the service as a foreground service
                    StartForeground(SERVICE_RUNNING_NOTIFICATION_ID, notification);


                    foreach (var cb in _startServiceCallbacks)
                    {
                        try
                        {
                            cb(_poc1);
                        }
                        catch
                        {
                        }
                    }
                    _startServiceCallbacks.Clear();
                }
            }
            //else if (intent.Action.Equals(Constants.ACTION_STOP_SERVICE))
            //{
            //    Log.Info(TAG, "OnStartCommand: The service is stopping.");
            //    timestamper = null;
            //    StopForeground(true);
            //    StopSelf();
            //    isStarted = false;

            //}
            //else if (intent.Action.Equals(Constants.ACTION_RESTART_TIMER))
            //{
            //    Log.Info(TAG, "OnStartCommand: Restarting the timer.");
            //    timestamper.Restart();

            //}

            // This tells Android not to restart the service if it is killed to reclaim resources.
            return StartCommandResult.Sticky;
        }


        public override IBinder OnBind(Intent intent)
        {
            // Return null because this is a pure started service. A hybrid service would return a binder that would
            // allow access to the GetFormattedStamp() method.
            return null;
        }


        public override void OnDestroy()
        {
            // We need to shut things down.
          //  Log.Debug(TAG, GetFormattedTimestamp() ?? "The TimeStamper has been disposed.");
         //   Log.Info(TAG, "OnDestroy: The started service is shutting down.");

            // Stop the handler.
         //   handler.RemoveCallbacks(runnable);

            // Remove the notification from the status bar.
            var notificationManager = (NotificationManager)GetSystemService(NotificationService);
            notificationManager.Cancel(SERVICE_RUNNING_NOTIFICATION_ID);

            //   timestamper = null;
            //   isStarted = false;
            StopSelf();
            StopForeground(true);


            if (_poc1 != null)
                _poc1.Dispose();


            base.OnDestroy();
        }

      


        /// <summary>
        /// Builds a PendingIntent that will display the main activity of the app. This is used when the 
        /// user taps on the notification; it will take them to the main activity of the app.
        /// </summary>
        /// <returns>The content intent.</returns>
        PendingIntent BuildIntentToShowMainActivity()
        {
            var notificationIntent = new Intent(this, typeof(MainActivity));
            notificationIntent.SetAction(ACTION_MAIN_ACTIVITY);
            notificationIntent.SetFlags(ActivityFlags.SingleTop | ActivityFlags.ClearTask);
           // notificationIntent.PutExtra(Constants.SERVICE_STARTED_KEY, true);

            var pendingIntent = PendingIntent.GetActivity(this, 0, notificationIntent, PendingIntentFlags.UpdateCurrent);
            return pendingIntent;
        }

        /// <summary>
        /// Builds a Notification.Action that will instruct the service to restart the timer.
        /// </summary>
        /// <returns>The restart timer action.</returns>
        //Notification.Action BuildRestartTimerAction()
        //{
        //    var restartTimerIntent = new Intent(this, GetType());
        //    restartTimerIntent.SetAction(Constants.ACTION_RESTART_TIMER);
        //    var restartTimerPendingIntent = PendingIntent.GetService(this, 0, restartTimerIntent, 0);

        //    var builder = new Notification.Action.Builder(Resource.Drawable.ic_action_restart_timer,
        //                                      GetText(Resource.String.restart_timer),
        //                                      restartTimerPendingIntent);

        //    return builder.Build();
        //}

        /// <summary>
        /// Builds the Notification.Action that will allow the user to stop the service via the
        /// notification in the status bar
        /// </summary>
        /// <returns>The stop service action.</returns>
        //Notification.Action BuildStopServiceAction()
        //{
        //    var stopServiceIntent = new Intent(this, GetType());
        //    stopServiceIntent.SetAction(Constants.ACTION_STOP_SERVICE);
        //    var stopServicePendingIntent = PendingIntent.GetService(this, 0, stopServiceIntent, 0);

        //    var builder = new Notification.Action.Builder(Android.Resource.Drawable.IcMediaPause,
        //                                                  GetText(Resource.String.stop_service),
        //                                                  stopServicePendingIntent);
        //    return builder.Build();

        //}
    }
}