using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Events;
using Family_Library.Revit;

namespace Family_Library.Services
{
    /// <summary>
    /// Starts interactive placement of a freshly loaded family, then brings the Family Library window back when the
    /// placement tool ends. Uses one Idling handler with two phases, because Revit does not allow subscribing to
    /// Idling from inside an Idling handler (unsubscribing is allowed):
    ///  1. Start: minimize the window and post the placement request.
    ///  2. Watch: Revit does not raise Idling while the placement tool is active (several instances can be placed),
    ///     so the next Idling after a short delay means the tool has ended → restore the window and unsubscribe.
    /// </summary>
    public static class DeferredPlacement
    {
        private enum Phase { None, Start, Watch }

        private static UIApplication _uiapp;
        private static ElementId _symbolId = ElementId.InvalidElementId;
        private static Phase _phase = Phase.None;
        private static System.DateTime _postedAt;

        public static void Start(UIApplication uiapp, ElementId symbolId)
        {
            if (uiapp == null || symbolId == null || symbolId == ElementId.InvalidElementId)
                return;

            _symbolId = symbolId;

            if (_phase == Phase.None)
            {
                _uiapp = uiapp;
                _uiapp.Idling += OnIdling;
            }
            // Already subscribed (still watching a previous placement): just start the new one on the next Idling.
            _phase = Phase.Start;
        }

        private static void OnIdling(object sender, IdlingEventArgs e)
        {
            if (_phase == Phase.Start)
                StartPlacement(e);
            else if (_phase == Phase.Watch)
                WatchPlacement(e);
            else
                Stop(sender);
        }

        private static void StartPlacement(IdlingEventArgs e)
        {
            try
            {
                var uidoc = _uiapp?.ActiveUIDocument;
                var sym = uidoc?.Document.GetElement(_symbolId) as FamilySymbol;
                _symbolId = ElementId.InvalidElementId;
                if (sym == null)
                {
                    Stop(_uiapp);
                    return;
                }

                // Minimize our window so Revit gets full focus and the options bar
                UiWindowHost.HideForPlacement();

                // Start placement using the Revit-native pipeline (options bar stays usable)
                uidoc.PostRequestForElementTypePlacement(sym);

                _postedAt = System.DateTime.UtcNow;
                _phase = Phase.Watch;
                e.SetRaiseWithoutDelay();
            }
            catch
            {
                Stop(_uiapp);
                UiWindowHost.RestoreAfterPlacement();
            }
        }

        private static void WatchPlacement(IdlingEventArgs e)
        {
            var elapsed = (System.DateTime.UtcNow - _postedAt).TotalMilliseconds;
            if (elapsed < 400)
            {
                // An Idling can arrive before the posted request has started; keep asking for the next one.
                e.SetRaiseWithoutDelay();
                return;
            }

            Stop(_uiapp);
            UiWindowHost.RestoreAfterPlacement();
        }

        private static void Stop(object app)
        {
            try
            {
                if (app is UIApplication a) a.Idling -= OnIdling;
            }
            catch
            {
            }
            _phase = Phase.None;
            _uiapp = null;
        }
    }
}
