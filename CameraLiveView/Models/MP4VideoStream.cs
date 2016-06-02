using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;
using NReco.VideoConverter;

namespace CameraLiveView.Models
{
    internal class QueueStream : Stream
    {
        private readonly BlockingCollection<byte[]> _queue = new BlockingCollection<byte[]>(new ConcurrentQueue<byte[]>(), 10);

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            throw new NotImplementedException();
        }

        public override void SetLength(long value)
        {
            throw new NotImplementedException();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            throw new NotImplementedException();
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            var buf = new byte[count];
            Array.Copy(buffer, offset, buf, 0, count);
            _queue.Add(buf);
        }

        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => 0;

        public override long Position
        {
            get { throw new NotImplementedException(); }
            set { throw new NotImplementedException(); }
        }

        public BlockingCollection<byte[]> Queue => _queue;

        protected override void Dispose(bool disposing)
        {
            _queue.Dispose();
            base.Dispose(disposing);
        }
    }

    internal class Mp4VideoStream
    {
        private readonly Camera _c;
        private readonly FFMpegConverter _converter = new FFMpegConverter();

        public Mp4VideoStream(string name)
        {
            // Log the info the converter prints. Hide when released
            _converter.LogReceived += (sender, args) => Debug.WriteLine(args.Data);
            // go get a camera, create only if not already set up
            _c = CameraManager.Instance.GetCamera(name);
        }

        public async Task WriteToStream(Stream outputStream, HttpContent content, TransportContext context)
        {
            // This is way too complex.  just to get around a problem in the converter.  can recreate ourselves to just
            // call ffmpeg correctly and get rid of much of this.
            using (var outputQs = new QueueStream())
            {
                var t = _converter.ConvertLiveMedia(
                            Format.mjpeg,
                            outputQs,
                            Format.mp4,
                            new ConvertSettings
                            {
                                // Took a while to get these just right.  the probezie and analyzeduration can probably go, but the rest
                                // is needed.  Some things can be tuned, like crf.  smaller numbers for better quality. 
                                // would love to be ablel to use vfr below, but not sure it will work right. 
                                CustomInputArgs = "-use_wallclock_as_timestamps 1 -vsync cfr -re ",
                                CustomOutputArgs ="-g 2 -crf 23 -tune zerolatency -preset ultrafast -movflags frag_keyframe+empty_moov -pix_fmt yuvj420p "
                            });

                // tell it to start.
                t.Start();

                var cts = new CancellationTokenSource();
                var writer = Task.Run(
                    () =>
                    {
                        try
                        {
                            // grab each frame
                            foreach (var bytes in _c.Frames.Latest())
                            {
                                if (cts.Token.IsCancellationRequested)
                                    return;
                                // and write it to the encoder.
                                t.Write(bytes, 0, bytes.Length);
                            }
                        }
                        catch
                        {
                        }
                    }, 
                    cts.Token);

                var copier = Task.Run(
                    async () =>
                    {
                        try
                        {
                            foreach (var buf in outputQs.Queue.GetConsumingEnumerable())
                            {
                                if (cts.Token.IsCancellationRequested)
                                    return;
                                await outputStream.WriteAsync(buf, 0, buf.Length, cts.Token);
                            }
                        }
                        catch
                        {
                        }

                    }, cts.Token);

                await copier;

                cts.Cancel();
                t.Stop(true);

                await writer;
            }

           // The ffmpeg wrapper when we call Start fires up a background thread.  When its running, it sits there
           // grabbing data from the output of ffmpeg and writing it to our output stream.  when that was the http connection
           // there was a problem.  when the browser shutdown, the writes failed, that caused an exception in the background thread
           // and that was unhandled, so it just stopped the entire server with an unhandled exception.
           //
           // The above was a tweak to prevent this.  The background thread that is reading from ffmpeg is writing into an 
           // intermediate stream, the QueueStream, that just drops the byte arrays into a queue.  Once we start ffmpeg, we then fire 
           // up two other tasks, one to grab each of the frames we get from the camera and to write them into the ffmpeg
           // engine, and the other to read from that QueueStream and write the values off to the http clinet.  if the browser 
           // closes, the writing thread will generate an exception, which we will ignore, but we will stop the copier thread
           // the await copier will then let us move on, and we will then cancel the frame writer, stop the encoder, then wait for it to
           // all exit quietly.
           //
           // this prevents the app from blowing up.
           // 
           // A cleaner way would be to rewrite our own ffmpeg wrapper rather than using the one we have here.  its just a simple wrappre around
           // a process with some input and output stream work.  It would be easy enough to do, and we could make it propery async and 
           // so on.  But, thats for later, maybe, if really needed.
        }
    }
}