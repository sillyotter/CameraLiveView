using System.Net.Http;
using System.Web.Http;
using System.Web.Http.Hosting;
using System.Web.Http.WebHost;

namespace CameraLiveView
{
    public class NoBufferPolicySelector : WebHostBufferPolicySelector
    {
        public override bool UseBufferedInputStream(object hostContext)
        {
            return false;
        }

        public override bool UseBufferedOutputStream(HttpResponseMessage response)
        {
            return false;
        }
    }

    public static class WebApiConfig
    {
        public static void Register(HttpConfiguration config)
        {
            GlobalConfiguration.Configuration.Services.Replace(typeof(IHostBufferPolicySelector), new NoBufferPolicySelector());

            config.MapHttpAttributeRoutes();

            config.Routes.MapHttpRoute(
                name: "DefaultApi",
                routeTemplate: "api/{controller}/{id}",
                defaults: new { id = RouteParameter.Optional }
            );
        }
    }
}
