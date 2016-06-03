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

        private const string InputOptions = "-f mjpeg -use_wallclock_as_timestamps 1 -vsync cfr -re";
        private const string OutputOptions = "-f mp4 -g 2 -crf 23 -tune zerolatency -preset ultrafast -movflags frag_keyframe+empty_moov -pix_fmt yuvj420p";

        private static readonly string FFMpegDirectory = AppDomain.CurrentDomain.BaseDirectory + "bin";
        private static readonly string FFMpegExeLocation = Path.Combine(FFMpegDirectory, "ffmpeg.exe");
        private static readonly ProcessStartInfo StartInfo = new ProcessStartInfo(FFMpegExeLocation, $"{InputOptions} -i \"-\" {OutputOptions} \"-\"")
        {
            CreateNoWindow = true,
            ErrorDialog = false,
            UseShellExecute = false,
            RedirectStandardError = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            WorkingDirectory = FFMpegDirectory
        };

        public FFMpegWrapper(IObservable<Tuple<byte[], int>> frames)
        {
            _p = Process.Start(StartInfo);

            _task = Task.Run(
                async () =>
                      {
                          try
                          {
                              foreach (var item in frames.Latest()) // as in jpeg stream. all time is spent waiting for frames here, meaning weve got this thing pretty well tuned
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
            => _p.StandardOutput.BaseStream.ReadAsync(data, offset, count);

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