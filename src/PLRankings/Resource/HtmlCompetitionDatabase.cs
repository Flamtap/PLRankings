﻿using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using HtmlAgilityPack;
using PLRankings.Models;

namespace PLRankings.Resource
{
    public class HtmlCompetitionDatabase : ICompetitionDatabase
    {
        //TODO: move to config
        private const string CpuLifterDatabaseUrl =
            "http://www.powerlifting.ca/cpu/index.php/competitors/lifter-database";

        private readonly HttpClient _httpClient;

        public HtmlCompetitionDatabase(HttpClient httpClient)
        {
            _httpClient = httpClient;
        }

        #region Implementation of ICompetitionDatabase

        public async Task<IEnumerable<CompetitionResult>> QueryAsync(CompetitionResultQuery query)
        {
            using var response = await _httpClient.SendAsync(CreateHttpRequest(query));

            response.EnsureSuccessStatusCode();

            var html = await response.Content.ReadAsStringAsync();

            var htmlDocument = new HtmlDocument();
            htmlDocument.LoadHtml(html);

            var resultsTableNode = htmlDocument.DocumentNode.SelectNodes("//form/table")
                .SingleOrDefault(n => n.Id == "lifter_database");

            if (resultsTableNode == null)
                return new CompetitionResult[]{};

            var resultsRowNodes = resultsTableNode.ChildNodes.Skip(2).ToList();

            double ParseDoubleOrDefault(string value)
            {
                return double.TryParse(value, out var result) ? result : default;
            }

            var results = resultsRowNodes.Select(rrn => new CompetitionResult
            {
                ContestName = rrn.ChildNodes[0].InnerText,
                Date = DateTime.ParseExact(rrn.ChildNodes[1].InnerText, new []{"dd-MMM-yy", "d-MMM-yy" }, new DateTimeFormatInfo()),
                Location = rrn.ChildNodes[2].InnerText,
                CompetitionType = rrn.ChildNodes[3].InnerText switch
                {
                    "All" => CompetitionType.ThreeLift,
                    "Single" => CompetitionType.BenchOnly,
                    _ => CompetitionType.Unknown
                },
                Sex = rrn.ChildNodes[4].InnerText == "M" ? Sex.Male : Sex.Female,
                AthleteName = AliasDictionary.Aliases.TryGetValue(rrn.ChildNodes[5].InnerText, out var realName)
                    ? realName
                    : rrn.ChildNodes[5].InnerText,
                Province = rrn.ChildNodes[6].InnerText,
                Bodyweight = ParseDoubleOrDefault(rrn.ChildNodes[7].InnerText),
                WeightClass = rrn.ChildNodes[8].InnerText,
                AgeCategory = rrn.ChildNodes[9].InnerText,
                Squat = ParseDoubleOrDefault(rrn.ChildNodes[10].InnerText),
                Bench = ParseDoubleOrDefault(rrn.ChildNodes[11].InnerText),
                Deadlift = ParseDoubleOrDefault(rrn.ChildNodes[12].InnerText),
                Total = ParseDoubleOrDefault(rrn.ChildNodes[13].InnerText),
                Points = ParseDoubleOrDefault(rrn.ChildNodes[14].InnerText),
                Equipment = rrn.ChildNodes[15].InnerText switch
                {
                    "no" => Equipment.SinglePly,
                    "yes" => Equipment.Raw,
                    _ => throw new ArgumentException($"Unknown equipment type '{rrn.ChildNodes[15].InnerText}.")
                }
            }).ToList();

            return results.OrderByDescending(r => r.Points).ToArray();
        }

        #endregion

        private static HttpRequestMessage CreateHttpRequest(CompetitionResultQuery resultQuery)
        {
            return new HttpRequestMessage(HttpMethod.Post, CpuLifterDatabaseUrl)
            {
                Content = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    { "style", resultQuery.CompetitionType switch
                    {
                        CompetitionType.BenchOnly => "single",
                        CompetitionType.ThreeLift => "all",
                        _ => null
                    }},
                    { "gender", resultQuery.Sex == Sex.Male ? "M" : "F" },
                    { "province", resultQuery.Province },
                    { "age_category", resultQuery.AgeCategory },
                    { "weightclass_new", resultQuery.WeightClass },
                    { "year", resultQuery.Year.HasValue ? resultQuery.Year.ToString() : null },
                    { "name", resultQuery.AthleteName },
                    { "unequipped", resultQuery.Equipment.HasValue
                        ? resultQuery.Equipment == Equipment.Raw ? "yes" : "no"
                        : null },
                    { "contest", resultQuery.ContestName },
                    { "submit", "Search" } // Required to actually execute a search for some reason
                })
            };
        }
    }
}
