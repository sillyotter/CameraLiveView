using System.Net.Http;
using System.Net.Http.Headers;
using System.Web.Http;
using CameraLiveView.Models;

namespace CameraLiveView.Controllers
{
    /// <summary>
    /// CameraController is responsible for handling any requests for camera data to be captured
    /// and returned as MJPEG.  The real work is done in the JpegImageStream class
    /// </summary>
    public class CameraController : ApiController
    {
        public HttpResponseMessage Get(string id)
        {
            var imageStream = new JpegImageStream(id);
            var response = Request.CreateResponse();
            response.Content = new PushStreamContent(imageStream.WriteToStream);
            response.Content.Headers.Remove("Content-Type");
            response.Content.Headers.TryAddWithoutValidation("Content-Type", "multipart/x-mixed-replace;boundary=" + JpegImageStream.Boundary);
            response.Headers.CacheControl = new CacheControlHeaderValue
            {
                NoCache = true,
            };

            return response;
        }
    }
}
