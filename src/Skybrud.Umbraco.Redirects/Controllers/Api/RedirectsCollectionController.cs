using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Asp.Versioning;
using Humanizer.Localisation;
using Lucene.Net.Util;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Skybrud.Essentials.Enums;
using Skybrud.Essentials.Time;
using Skybrud.Umbraco.Redirects.Controllers.Api.BackOffice;
using Skybrud.Umbraco.Redirects.Helpers;
using Skybrud.Umbraco.Redirects.Models;
using Skybrud.Umbraco.Redirects.Models.Api;
using Skybrud.Umbraco.Redirects.Services;
using Skybrud.Umbraco.Redirects.Text.Json;
using Umbraco.Cms.Api.Management.Factories;
using Umbraco.Cms.Api.Management.ViewModels.RedirectUrlManagement;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Core.Web;
using Umbraco.Cms.Web.Common.Authorization;
using Umbraco.Cms.Web.Common.Routing;

namespace Skybrud.Umbraco.Redirects.Controllers.Api;

#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member

[ApiController]
[BackOfficeRoute("forte/redirects")]
[ApiVersion("1.0")]
[ApiExplorerSettings(GroupName = "All Redirects Collection")]
public class RedirectsCollectionController : Controller {

    private readonly IRedirectsService _redirectsService;
    private readonly IRedirectUrlService _redirectUrlService;
    private readonly IRedirectUrlPresentationFactory _redirectUrlPresentationFactory;
    #region Constructors

    public RedirectsCollectionController(IRedirectsService redirectsService, IRedirectUrlService redirectUrlService, IRedirectUrlPresentationFactory redirectUrlPresentationFactory) {
        _redirectsService = redirectsService;
        _redirectUrlService = redirectUrlService;
        _redirectUrlPresentationFactory = redirectUrlPresentationFactory;
    }
    #endregion

    /// <summary>
    /// Gets a list of all redirects in the system (custom + default).
    /// </summary>
    /// <returns>A list of redirects.</returns>
    [HttpGet]
    public object Index() {

        // Make the search for redirects via the redirects service
        var customRedirects = _redirectsService.GetRedirects();

        IList<IRedirectUrl> defaultRedirects = new List<IRedirectUrl>();

        long pageIndex = 0;

        while (true) {

            IEnumerable<IRedirectUrl> redirectPortion = _redirectUrlService.GetAllRedirectUrls(pageIndex, 50, out long total);

            if (redirectPortion.Any()) {
                defaultRedirects.AddRange(redirectPortion);
                pageIndex++;
            } else {
               break;
            }
        }
        IEnumerable<RedirectUrlResponseModel> defaultRedirectItems = _redirectUrlPresentationFactory.CreateMany(defaultRedirects);

        IList<RedirectModel> allRedirects = new List<RedirectModel>();

        //Combine into a single collection
        allRedirects.AddRange(customRedirects.Select(cr => new RedirectModel() { Destination = cr.Destination.Url, Target = cr.Path, IsPermanent = cr.IsPermanent }));
        allRedirects.AddRange(defaultRedirectItems.Select(dr => new RedirectModel() {Destination = dr.DestinationUrl, Target = dr.OriginalUrl, IsPermanent = true}));


        return allRedirects;

    }
}

public class RedirectModel {

    [JsonProperty("target")]
    [JsonPropertyName("target")]
    public string Target { get; set; }

    [JsonProperty("destination")]
    [JsonPropertyName("destination")]
    public string Destination { get; set; }

    [JsonProperty("permanent")]
    [JsonPropertyName("permanent")]
    public bool IsPermanent { get; set; }

}
