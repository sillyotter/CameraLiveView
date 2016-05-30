using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Reactive.Linq;
using System.Threading.Tasks;

namespace CameraLiveView.Models
{
    /// <summary>
    /// This is a wrapper for the camera.  There should be only one per real camera if things work right
    /// This version is a simulator, it makes its own images.  The real one woul dbe modified to fetch the values
    /// from the real physical camera with a http client.
    /// </summary>
    internal class Camera
    {
        private readonly string _name;

        // this allows you to pass in the rate.  not sure what else youd need in here
        // the camera manager cache kinda makes it awkward, the future fetches of this named
        // camera will just use the old one, will ignore the rate passed in.  probably should
        // set all camera settings in some kind of a start up loadable config file or something
        // I just wanted to be able to show two cameras on the page at two different frame rates
        // to prove it would work
        public Camera(string name, int r)
        {
            _name = name;

            // Create an observable to emit frames.
            Frames = Observable.Create<byte[]>(
                (obs, tok) =>
                {
                    System.Diagnostics.Debug.WriteLine($"Creating fake camera {name}");
                    return Task.Run(
                        async () =>
                              {
                                  // this sits here and generates frames of jpeg, your app would contact the camera
                                  // and fetch the stream, grab each jpeg frame, and push it to the place below
                                  try
                                  {
                                      var i = 0;
                                      while (!tok.IsCancellationRequested) 
                                      // when all the subscriptions go away, this token will 
                                      // be canceled, use this to quite your http fetching when were done.
                                      {
                                          // we instead will cheat, create a simple 200x200 image
                                          using (var img = new Bitmap(200, 200))
                                          {
                                              using (var g = Graphics.FromImage(img))
                                              {
                                                  // draw some text on it
                                                  string text = $"Cam: {_name}  Fr: {i++}";

                                                  var drawFont = new Font("Arial", 10);
                                                  var drawBrush = new SolidBrush(Color.White);
                                                  var stringPonit = new PointF(0, 0);

                                                  g.DrawString(text, drawFont, drawBrush, stringPonit);
                                              }

                                              // save it off as a jpeg
                                              using (var ms = new MemoryStream())
                                              {
                                                  img.Save(ms, ImageFormat.Jpeg);
                                                  await ms.FlushAsync();
                                                  // This is where you push the image to the listeners.
                                                  // the results of your http fetch would be pushed here.
                                                  obs.OnNext(ms.ToArray());
                                                  await Task.Delay(TimeSpan.FromSeconds(1.0/r));
                                                  // delay this a bit, to only generate some N frames a second.
                                                  // only needed for fake camera.  Here we are set to what ever was passed in on initial creation, 10 by default
                                              }
                                          }
                                      }
                                      obs.OnCompleted();
                                  }
                                  catch (Exception e)
                                  {
                                      obs.OnError(e);
                                  }
                              }, tok);
                })
                .Publish()
                .RefCount(); 
            // publish/refcount caues us to make a single image generator, and share it, but when were all done with it and no one wants it
            // we will shut it down.  The first subscribe will cause the above thread to start, any future subscriptions
            // will just share the same output, and then finally, when the last observer unsubscribes, it will stop the thing. 
        }

        public IObservable<byte[]> Frames { get; }
    }
}