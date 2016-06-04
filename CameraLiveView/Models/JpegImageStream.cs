using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Text;
using System.Threading.Tasks;
using NLog;

namespace CameraLiveView.Models
{
    internal class JpegImageStream
    {
        private static readonly Logger Log = LogManager.GetCurrentClassLogger();

        public const string Boundary = "CMLV";
        private static readonly byte[] NewLine = Encoding.ASCII.GetBytes("\r\n");

        private readonly Camera _c;

        public JpegImageStream(string name)
        {
            // Fetch a camera from the static camera repository.  If needed, it will create one.
            _c = CameraManager.Instance.GetCamera(name);
        }

        public async Task WriteToStream(Stream outputStream, HttpContent content, TransportContext context)
        {
            try
            {
                // The camera exposes an IObservable called Frames.  Latest will provide us with an IEnumreable that
                // always returns the most recent value, or blocks if there has ben none since the last fetch.
                // This is so that if we are generating images faster than we can send them, we will drop the unneeded ones
                foreach (var item in _c.Frames.ObserveOn(TaskPoolScheduler.Default).Latest())
                {
                    var bytes = item.Item1;
                    var len = item.Item2;

                    var headerData = Encoding.UTF8.GetBytes($"--{Boundary}\r\nContent-Type: image/jpeg\r\nContent-Length: {len}\r\n\r\n");

                    await outputStream.WriteAsync(headerData, 0, headerData.Length).ConfigureAwait(false);
                    await outputStream.WriteAsync(bytes, 0, len).ConfigureAwait(false);
                    await outputStream.WriteAsync(NewLine, 0, NewLine.Length).ConfigureAwait(false);
                    await outputStream.FlushAsync().ConfigureAwait(false);

                }
                // after performance tuning, profiing says that the only slow thing we do that takes any measureable time is wait
                // for frames above.  We could stop, and just subscribe to it, and have it write every frame out, but if the outbound stream
                // is too slow, then we will start buffering up old frames trying to send them.  We really need latest here to allow us to 
                // drop frames we arent in a position to send.  I just wish it wasnt a blocking call, if it were async, that would be great.
            }
            finally
            {
               Log.Debug("done sending frames to http client");
            }
        }
    }
}