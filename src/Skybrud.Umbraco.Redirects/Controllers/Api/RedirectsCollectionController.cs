using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Asp.Versioning;
using Humanizer.Localisation;
using Lucene.Net.Util;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Skybrud.Essentials.Enums;
using Skybrud.Essentials.Strings.Extensions;
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


    /// <summary>
    /// Accepts a CSV file with first two columns as old URL and new URL. The CSV file should be formatted as follows:
    /// 1st column: Old URL (e.g. /old-url)
    /// 2nd column: New URL (e.g. /new-url)
    /// </summary>
    [HttpPost("import-redirects-csv")]
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> ImportRedirectsFromCsv(IFormFile file) {
        if (file == null || file.Length == 0)
            return BadRequest("No file uploaded.");

        var added = new List<string>();
        var skipped = new List<string>();

        // Read all existing custom redirects (Old URLs)
        var existingRedirects = _redirectsService.GetRedirects();
        var existingOldUrls = new HashSet<string>(
            existingRedirects.Select(r => r.Url.Trim().ToLowerInvariant())
        );

        using (var stream = file.OpenReadStream())
        using (var reader = new StreamReader(stream)) {
            string? line;
            while ((line = await reader.ReadLineAsync()) != null) {
                // Skip empty lines
                if (string.IsNullOrWhiteSpace(line)) continue;

                // Split CSV (simple split, for more complex CSVs use a library)
                var columns = line.Split(',');
                if (columns.Length < 2) continue;

                var oldUrl = columns[0].Trim();
                var newUrl = columns[1].Trim();

                // Split the URL (path) and query string
                oldUrl.Split('?', out string oldCleanUrl, out string? oldQueryString);
                newUrl.Split('?', out string newCleanUrl, out string? newQueryString);

                var oldUrlAbsolutePath = GetAbsolutePath(oldCleanUrl);
                var newUrlAbsolutePath = GetAbsolutePath(newCleanUrl);

                if (string.IsNullOrWhiteSpace(oldUrl) || string.IsNullOrWhiteSpace(newUrl))
                    continue;

                // Check if redirect already exists
                if (existingOldUrls.Contains(oldUrlAbsolutePath.ToLowerInvariant())) {
                    skipped.Add(oldCleanUrl);
                    continue;
                }

                // Create and add the redirect
                var options = new AddRedirectOptions {
                    OriginalUrl = oldUrlAbsolutePath,
                    Destination = new RedirectDestination {
                        Url = newUrlAbsolutePath,
                        Query = newQueryString,
                        Type = RedirectDestinationType.Url
                    },
                    Type = RedirectType.Permanent,
                    ForwardQueryString = false,
                    RootNodeKey = Guid.Empty // or set as needed
                };

                try {
                    var redirect = _redirectsService.AddRedirect(options);
                    redirect.QueryString = oldQueryString;
                    _redirectsService.SaveRedirect(redirect);

                    added.Add(oldCleanUrl);
                    existingOldUrls.Add(oldCleanUrl.ToLowerInvariant());
                } catch (Exception ex) {
                    // Optionally log or collect errors
                    skipped.Add(oldUrl + " (error: " + ex.Message + ")");
                }
            }
        }

        return Ok(new {
            addedCount = added.Count,
            skippedCount = skipped.Count,
            added,
            skipped
        });
    }


    private static string GetAbsolutePath(string url) {
        return !Uri.TryCreate(url, UriKind.Absolute, out Uri uri) ? null : uri.AbsolutePath;
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
