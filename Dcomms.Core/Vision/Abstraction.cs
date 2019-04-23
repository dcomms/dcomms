using System;
using System.Collections.Generic;
using System.Text;

namespace Dcomms.Vision
{
    public enum AttentionLevel
    {
        /// <summary>
        /// details that can easily overload the RAM (sent every 10ms)
        /// </summary>
        deepDetail,
        /// <summary>
        /// "sent request X" "received response X totally 10 times"
        /// </summary>
        detail,
        /// <summary>
        /// "pressed button X", "started app", "closed app after 10 minutes"
        /// </summary>
        guiActivity,
        /// <summary>
        /// (possible) problem expressed by user: closed app with no clicks, sent a bad feedback "app is not clear" "app does not work"
        /// </summary>
        guiPain,
        /// <summary>
        /// signal of possible future problems, self-tested in code
        /// "abnormal delay in X"  
        /// </summary>
        lightPain,
        /// <summary>
        /// self-tested problem; an important problem but the application still works and is able to provide some value to user
        /// </summary>
        mediumPain,
        strongPain,
        /// <summary>
        /// application crashed after non-recoverable error,  like memory leak
        /// </summary>
        death
    }

    /// <summary>
    /// provides link from executing code to developer
    /// sends various signals to developer: pain
    /// </summary>
    public class DevelopersVisionChannel
    {
        public AttentionLevel HasAttentionTo(string objectName, string sourceCodePlaceId)
        {
            throw new NotImplementedException();
        }
        public void Emit(string objectName, string sourceCodePlaceId, AttentionLevel level, string message)
        {

        }
        public void Emit(string objectName, string sourceCodePlaceId, double value, double? lightPainThresholdL, double? painThresholdL)
        {

        }
    }
}
