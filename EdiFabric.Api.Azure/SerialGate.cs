using EdiFabric.Native.X12;

namespace EdiFabric.Api.Azure
{
    /// <summary>
    /// ediFabric Native keeps one license and one model map per process, and each request may send its own serial in
    /// Ocp-Apim-Subscription-Key. The gate runs every native call under the serial and the model map of its own request:
    /// calls for the serial in use run in parallel, and a call for another serial waits for them, switches the license
    /// (ensure_token) and the map (set_map), and runs on its own. Run only synchronous native work inside it.
    /// </summary>
    public static class SerialGate
    {
        /// <summary>ensure_token gets a new token when the cached one expires within this many seconds.</summary>
        private const int TokenSeconds = 3600;
        /// <summary>How often the serial in use is checked again, so its token is refreshed before it expires.</summary>
        private static readonly TimeSpan Recheck = TimeSpan.FromMinutes(15);

        private static readonly ReaderWriterLockSlim _lock = new();
        private static ILocalModelsService _models = new LocalModelsService();
        private static string? _serial;
        private static DateTime _checkedUtc;

        /// <summary>Licenses the process with the configured serial. A failure is logged, not thrown: the first request tries again.</summary>
        public static void Init(ILocalModelsService models, string serial)
        {
            _models = models;
            try
            {
                Run(serial, () => true);
            }
            catch (EdiFabricException ex)
            {
                Console.WriteLine($"ediFabric Native isn't licensed yet: {ex.Message}");
            }
        }

        /// <summary>Runs work under the license and model map of serial.</summary>
        public static T Run<T>(string serial, Func<T> work)
        {
            _lock.EnterReadLock();
            try
            {
                if (_serial == serial && DateTime.UtcNow - _checkedUtc < Recheck)
                    return work();
            }
            finally
            {
                _lock.ExitReadLock();
            }

            _lock.EnterWriteLock();
            try
            {
                if (_serial != serial || DateTime.UtcNow - _checkedUtc >= Recheck)
                {
                    // A failed switch can leave the previous license in place, so nothing is in use until it succeeds.
                    var mapChanges = _serial != serial;
                    _serial = null;
                    EdiFabricX12.EnsureToken(serial, TokenSeconds);
                    if (mapChanges)
                        EdiFabricX12.SetMap(_models.MapFor(serial));
                    _serial = serial;
                    _checkedUtc = DateTime.UtcNow;
                }

                return work();
            }
            finally
            {
                _lock.ExitWriteLock();
            }
        }
    }
}
