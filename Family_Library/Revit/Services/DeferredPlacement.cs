using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Events;
using Family_Library.Revit;

namespace Family_Library.Services
{
    public static class DeferredPlacement
    {
        private static UIApplication _uiapp;
        private static ElementId _symbolId = ElementId.InvalidElementId;
        private static bool _armed = false;

        public static void Start(UIApplication uiapp, ElementId symbolId)
        {
            if (uiapp == null || symbolId == null || symbolId == ElementId.InvalidElementId)
                return;

            _uiapp = uiapp;
            _symbolId = symbolId;

            if (_armed) return;

            _armed = true;
            _uiapp.Idling += OnIdling;
        }

        private static void OnIdling(object sender, IdlingEventArgs e)
        {
            try
            {
                if (_uiapp != null)
                    _uiapp.Idling -= OnIdling;

                _armed = false;

                var uidoc = _uiapp?.ActiveUIDocument;
                if (uidoc == null) return;

                var doc = uidoc.Document;
                var sym = doc.GetElement(_symbolId) as FamilySymbol;
                if (sym == null) return;

                // Hide our window so Revit gets full focus/options
                UiWindowHost.HideForPlacement();

                // Start placement using Revit-native pipeline (options bar stays usable)
                uidoc.PostRequestForElementTypePlacement(sym);
            }
            catch
            {
            }
            finally
            {
                _uiapp = null;
                _symbolId = ElementId.InvalidElementId;
            }
        }
    }
}
