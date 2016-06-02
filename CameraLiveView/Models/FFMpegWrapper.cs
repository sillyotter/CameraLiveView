using System;
using System.Diagnostics;
using System.IO;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace CameraLiveView.Models
{
    public class FFMpegWrapper : IDisposable
    {
        private CancellationTokenSource _cts = new CancellationTokenSource();

        private Process _p;
        private Task _task;

        public FFMpegWrapper(IObservable<Tuple<byte[], int>> frames)
        {
            //-y -loglevel info // if you want log details from mmpeg and set up a listener for them.  comes out stderr or the errordatarecieved event
            var dirname = AppDomain.CurrentDomain.BaseDirectory + "bin";
            var exeloc = Path.Combine(dirname, "ffmpeg.exe");
            var pinfo = new ProcessStartInfo(
                exeloc,
                "-f mjpeg -use_wallclock_as_timestamps 1 -vsync cfr -re   -i \"-\"  -f mp4  -g 2 -crf 23 -tune zerolatency -preset ultrafast -movflags frag_keyframe+empty_moov -pix_fmt yuvj420p  \"-\"")
                        {
                            CreateNoWindow = true,
                            ErrorDialog = false,
                            UseShellExecute = false,
                            RedirectStandardError = false,
                            RedirectStandardInput = true,
                            RedirectStandardOutput = true,
                            WorkingDirectory = dirname
                        };

            _p = Process.Start(pinfo);

            _task = Task.Run(
                async () =>
                      {
                          try
                          {
                              foreach (var item in frames.Latest())
                              {
                                  var frame = item.Item1;
                                  var len = item.Item2;
                                  await _p.StandardInput.BaseStream.WriteAsync(frame, 0, len, _cts.Token).ConfigureAwait(false);
                                  await _p.StandardInput.BaseStream.FlushAsync(_cts.Token).ConfigureAwait(false);
                              }
                          }
                          catch(Exception ex)
                          {
                              Console.WriteLine(ex);
                          }
                      },
                _cts.Token);

        }
        
        public Task<int> Read(byte[] data, int offset, int count)
        {
            if (_p.HasExited)
            {
                throw new EndOfStreamException("ffmpeg is no longer running");
            }
            return _p.StandardOutput.BaseStream.ReadAsync(data, offset, count);
        }

        public void Dispose()
        {
            if (_p != null)
            {
                if (!_p.HasExited)
                {
                    _p.Kill();
                    _p.WaitForExit();
                }
                _p.Dispose();
                _p = null;
            }

            if (_task != null && _cts != null)
            {
                _cts.Cancel();
                _task.Wait();
                _task = null;
                _cts = null;
            }
        }
    }
}