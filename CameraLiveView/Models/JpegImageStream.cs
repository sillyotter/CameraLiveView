using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Reactive.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CameraLiveView.Models
{
    internal class JpegImageStream
    {
        private readonly Camera _c;
        public object Boundary { get; } = "CMLV";
        private static readonly byte[] NewLine = Encoding.UTF8.GetBytes("\r\n");

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
                foreach (var item in _c.Frames.Latest())
                {
                    var bytes = item.Item1;
                    var len = item.Item2;
                    // write them to the output stream
                    var header = $"--{Boundary}\r\nContent-Type: image/jpeg\r\nContent-Length: {len}\r\n\r\n";
                    var headerData = Encoding.UTF8.GetBytes(header);
                    await outputStream.WriteAsync(headerData, 0, headerData.Length).ConfigureAwait(false);
                    await outputStream.WriteAsync(bytes, 0, len).ConfigureAwait(false);
                    await outputStream.WriteAsync(NewLine, 0, NewLine.Length).ConfigureAwait(false);
                    await outputStream.FlushAsync().ConfigureAwait(false);
                }
            }
            finally
            {
                Console.WriteLine("done sending frames to http client");
            }
        }
    }
}