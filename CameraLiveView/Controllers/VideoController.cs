using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Web;
using System.Web.Http;
using CameraLiveView.Models;

namespace CameraLiveView.Controllers
{
    /// <summary>
    /// Responsible for handling requets for camera as videos.  Will push back a mp4 stream.
    /// All the real work is in the Mp4VideoStream class.
    /// </summary>
    public class VideoController : ApiController
    {
        public HttpResponseMessage Get(string id)
        {
            var q = HttpUtility.ParseQueryString(Request.RequestUri.Query);
            var r = int.Parse(q.Get("fr") ?? "10");
            var vstr = new Mp4VideoStream(id, r);
            var response = Request.CreateResponse();
            response.Content = new PushStreamContent(vstr.WriteToStream, "video/mp4");

            // Not sure this is needed, nearly sure it isnt, but the idea is to try to prevent the clients from caching this stuff.
            response.Headers.CacheControl = new CacheControlHeaderValue
            {
                NoCache = true,
            };

            return response;
        }
    }
}