using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Text;
using System.Threading.Tasks;

namespace CameraLiveView.Models
{
    internal class JpegImageStream
    {
        private readonly Camera _c;
        public object Boundary { get; } = "XXXBoundaryXXX";
        private static readonly byte[] NewLine = Encoding.UTF8.GetBytes("\r\n");

        public JpegImageStream(string name)
        {
            // Fetch a camera from the static camera repository.  If needed, it will create one.
            _c = CameraManager.Instance.GetCamera(name);
        }

        public async Task WriteToStream(Stream outputStream, HttpContent content, TransportContext context)
        {
            var sub = new Subject<int>();

            // you can get rid of this later, just used to keep track of the difference betwene the rate we get them from the camera and the rate 
            // we send them to the http client.
            var disp = sub
                .Buffer(TimeSpan.FromSeconds(1))
                .ObserveOn(new EventLoopScheduler())
                .Subscribe(
                    x =>
                    {
                        System.Diagnostics.Debug.WriteLine($"Frames sent per second {x.Count}");
                    });

            try
            {
                // The camera exposes an IObservable called Frames.  Latest will provide us with an IEnumreable that
                // always returns the most recent value, or blocks if there has ben none since the last fetch.
                // This is so that if we are generating images faster than we can send them, we will drop the unneeded ones
                foreach (var bytes in _c.Frames.Latest())
                {
                    // write them to the output stream
                    var header = $"--{Boundary}\r\nContent-Type: image/jpeg\r\nContent-Length: {bytes.Length}\r\n\r\n";
                    var headerData = Encoding.UTF8.GetBytes(header);
                    await outputStream.WriteAsync(headerData, 0, headerData.Length);
                    await outputStream.WriteAsync(bytes, 0, bytes.Length);
                    await outputStream.WriteAsync(NewLine, 0, NewLine.Length);
                    await outputStream.FlushAsync();
                    sub.OnNext(1);
                }
            }
            finally
            {
                disp.Dispose();
                System.Diagnostics.Debug.WriteLine("done sending frames to http client");
            }


            // MJPEG only works with real browsers.  Its fast, starts immediately, and should be ok on bandwidth,
            // but it doesnt work with IE.  In theory there are active X controls that will do this, but I havent seen
            // any. also java controls, but thats worse than ActiveX.  

            // As ususal, IE is a pile of junk.  But, you probably have to support it. You could, perhaps, use this mjpeg version
            // when your code detects its on a real browser (webkit,firefox,chrome) and use the other version (videocontroller on down)
            // only of IE.
        }
    }
}