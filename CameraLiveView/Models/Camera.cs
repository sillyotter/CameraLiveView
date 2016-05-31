using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Reactive.Linq;
using System.Threading.Tasks;
using AForge.Video;

namespace CameraLiveView.Models
{
    /// <summary>
    /// This is a wrapper for the camera.  There should be only one per real camera if things work right
    /// This version is a simulator, it makes its own images.  The real one woul dbe modified to fetch the values
    /// from the real physical camera with a http client.
    /// </summary>
    internal class Camera
    {
        private readonly MJPEGStream _stream;

        public string Name { get; }

        public Camera(string name)
        {
            Name = name;

            // tranlate name to the needed url somehow. for now im using one found on
            // http://www.insecam.org/en/bycountry/US/
            // youd want to construct it correctly with logins, settings, etc.
            // and construct the url to point to the thing indicated by name.

            _stream = new MJPEGStream("http://73.0.175.10:80/mjpg/video.mjpg?COUNTER");

            Frames = Observable.Create<byte[]>(
                obs =>
                {
                    // So, this is a little annoying, as the aforge engine goes to the trouble of decoding 
                    // the jpeg into a bitmap.  for the mjpeg stream, we want it back to a jpeg
                    // and for the video we can use anything, but for now, the code is set to use a jpeg
                    // as well.  so we will have to take this bitmap and re-encode it back to a jpeg
                    // kind of wasteful, as it was already in the right format once.

                    var disp = Observable.FromEventPattern<NewFrameEventHandler, NewFrameEventArgs>(
                        handler => _stream.NewFrame += handler,
                        handler => _stream.NewFrame -= handler)
                        .Select(
                            x =>
                            {
                                using (var ms = new MemoryStream())
                                {
                                    x.EventArgs.Frame.Save(ms, ImageFormat.Jpeg);
                                    return ms.ToArray();
                                }
                            })
                        .Subscribe(obs);

                    _stream.Start();

                    return () =>
                           {
                               _stream.Stop();
                               _stream.WaitForStop();
                               disp.Dispose();
                           };

                })
                .Publish()
                .RefCount();

            // Create an observable to emit frames.
            //Frames = Observable.Create<byte[]>(
            //    (obs, tok) =>
            //    {
            //        System.Diagnostics.Debug.WriteLine($"Creating fake camera {name}");
            //        return Task.Run(
            //            async () =>
            //                  {
            //                      // this sits here and generates frames of jpeg, your app would contact the camera
            //                      // and fetch the stream, grab each jpeg frame, and push it to the place below
            //                      try
            //                      {
            //                          var i = 0;
            //                          while (!tok.IsCancellationRequested) 
            //                          // when all the subscriptions go away, this token will 
            //                          // be canceled, use this to quite your http fetching when were done.
            //                          {
            //                              // we instead will cheat, create a simple 200x200 image
            //                              using (var img = new Bitmap(200, 200))
            //                              {
            //                                  using (var g = Graphics.FromImage(img))
            //                                  {
            //                                      // draw some text on it
            //                                      string text = $"Cam: {_name}  Fr: {i++}";

            //                                      var drawFont = new Font("Arial", 10);
            //                                      var drawBrush = new SolidBrush(Color.White);
            //                                      var stringPonit = new PointF(0, 0);

            //                                      g.DrawString(text, drawFont, drawBrush, stringPonit);
            //                                  }

            //                                  // save it off as a jpeg
            //                                  using (var ms = new MemoryStream())
            //                                  {
            //                                      img.Save(ms, ImageFormat.Jpeg);
            //                                      await ms.FlushAsync();
            //                                      // This is where you push the image to the listeners.
            //                                      // the results of your http fetch would be pushed here.
            //                                      obs.OnNext(ms.ToArray());
            //                                      await Task.Delay(TimeSpan.FromSeconds(1.0/r));
            //                                      // delay this a bit, to only generate some N frames a second.
            //                                      // only needed for fake camera.  Here we are set to what ever was passed in on initial creation, 10 by default
            //                                  }
            //                              }
            //                          }
            //                          obs.OnCompleted();
            //                      }
            //                      catch (Exception e)
            //                      {
            //                          obs.OnError(e);
            //                      }
            //                  }, tok);
            //    })
            //    .Publish()
            //    .RefCount(); 
            //// publish/refcount caues us to make a single image generator, and share it, but when were all done with it and no one wants it
            // we will shut it down.  The first subscribe will cause the above thread to start, any future subscriptions
            // will just share the same output, and then finally, when the last observer unsubscribes, it will stop the thing. 
        }

        public IObservable<byte[]> Frames { get; }
    }
}