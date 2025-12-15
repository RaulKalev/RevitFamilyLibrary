using Autodesk.Revit.UI;

namespace Family_Library.Revit.ExternalEvents
{
    public static class ExternalEventBridge
    {
        public static LibraryTaskHandler Handler { get; private set; }
        public static ExternalEvent Event { get; private set; }

        public static void EnsureCreated()
        {
            if (Event != null) return;
            Handler = new LibraryTaskHandler();
            Event = ExternalEvent.Create(Handler);
        }
    }
}
