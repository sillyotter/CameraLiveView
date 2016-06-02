using System;
using System.Collections.Concurrent;

namespace CameraLiveView.Models
{
    /// <summary>
    /// A class to manage the sharing of cameras between different streams.
    /// </summary>
    internal class CameraManager //: MarshalByRefObject
    {
        private CameraManager()
        {
        }

        private static readonly Lazy<CameraManager> SingletonInstance = new Lazy<CameraManager>(
            () => new CameraManager());

        public static CameraManager Instance => SingletonInstance.Value;

        private readonly ConcurrentDictionary<string, Lazy<Camera>> _cameraStore = new ConcurrentDictionary<string, Lazy<Camera>>();

        public Camera GetCamera(string name)
        {
            // This will manage a thread safe camera store.  We will ever only have one camere object per name in here,
            // and can access them safely from multiple requests.
            //
            // This has a small issue.  You may remember I said there was some confusion about sharing data like this
            // in an IIS app that wouldnt exist in a self hosted web service app?  Well, the issue is that static variables
            // are shared across all the threads in an app domain, but IIS some times will create more than one app domain, based on load
            // or other issues.  You can tune this in IIS or in machine.config <processModel maxAppDomains="1" enable="true"/>.
            //
            // With one app domain, we get one camera store, and with one camera store, we can always be sure that we will have a single
            // shared camera for all request.  With the default IIS settings, you may have a few.  But that is still likely to only
            // happen under heavy load, and you will likely still only have a few, and a few camear objects is better than one per
            // client page.
            //
            // It may be possible to force this thing off into its own appdomain, to make sure that it is only every accessed from one
            // app domain and hence only ever has one camera, but that may be more complicated than needed. (and so far, doesnt work, see above)
            return _cameraStore.GetOrAdd(name, n => new Lazy<Camera>(() => new Camera(n))).Value;
        }
    }
}