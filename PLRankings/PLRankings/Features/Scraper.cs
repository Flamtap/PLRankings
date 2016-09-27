﻿using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Web;
using PLRankings.Models;

namespace PLRankings.Features
{
    public static class Scraper
    {
        public static IEnumerable<CompetitionResult> GetResults(DateTime seasonStartDate, DateTime seasonEndDate, IEnumerable<string> resultsUris)
        {
            string resultsString = string.Empty;

            foreach (Uri resultsUri in resultsUris.Select(x => new Uri(x)))
            {
                string raw = GetRawHtml(resultsUri);

                int indexOfFirstResult = raw.IndexOf("<TR bgColor=\"WHITE\">", StringComparison.Ordinal);
                int indexOfResultsEnd = raw.IndexOf("</TABLE>", StringComparison.Ordinal);

                if (indexOfFirstResult == -1)
                    continue;

                resultsString += raw.Substring(indexOfFirstResult, indexOfResultsEnd - indexOfFirstResult);
            }

            string[] rows = Regex.Split(resultsString, "\r\n|\r|\n");

            List<CompetitionResult> results = new List<CompetitionResult>();

            foreach (CompetitionResult result in
                rows.Where(r => !string.IsNullOrEmpty(r))
                    .Select(BuildResult).Where(cr => (cr.Date > seasonStartDate) && (cr.Date <= seasonEndDate))
                    .OrderByDescending(cr => cr.WilksPoints))
            {
                CompetitionResult existingResult =
                    results.SingleOrDefault(
                        cr => string.Equals(cr.LifterName, result.LifterName, StringComparison.OrdinalIgnoreCase));

                if (existingResult == null)
                {
                    results.Add(result);
                    continue;
                }

                if (existingResult.WilksPoints < result.WilksPoints)
                {
                    results.Remove(existingResult);
                    results.Add(result);
                }
            }

            return results;
        }

        private static string GetRawHtml(Uri resultsUri)
        {
            HttpWebRequest request = (HttpWebRequest) WebRequest.Create(resultsUri);
            HttpWebResponse response = (HttpWebResponse) request.GetResponse();

            if (response.StatusCode == HttpStatusCode.OK)
            {
                using (StreamReader reader = new StreamReader(response.GetResponseStream(),
                    Encoding.GetEncoding(response.CharacterSet)))
                {
                    return reader.ReadToEnd();
                }
            }

            throw new HttpException();
        }

        private static CompetitionResult BuildResult(string tableRow)
        {
            // 7 is the length of the <FONT> tags.
            int substringStart = tableRow.IndexOf("<FONT >", StringComparison.Ordinal) + 7;
            int substringLength = tableRow.LastIndexOf("</TD>", StringComparison.Ordinal) - substringStart - 7;

            string tableData = tableRow.Substring(substringStart, substringLength);

            string[] tableCells = tableData.Split(new[] {"</FONT></TD><TD><FONT >"},
                StringSplitOptions.RemoveEmptyEntries);

            CompetitionResult result = new CompetitionResult();

            result.ContestName = tableCells[0];

            try
            {
                result.Date = DateTime.ParseExact(tableCells[1], "dd-MMM-yy", CultureInfo.InvariantCulture);
            }
            catch (FormatException)
            {
                result.Date = DateTime.ParseExact(tableCells[1], "d-MMM-yy", CultureInfo.InvariantCulture);
            }
            
            result.Location = tableCells[2].Replace(",", string.Empty);
            result.ContestType = tableCells[3];
            result.Gender = tableCells[4];
            result.LifterName = tableCells[5];
            result.Province = tableCells[6];
            result.Bodyweight = double.Parse(tableCells[7]);
            // unused column for retired bodyweight classes
            result.Class = tableCells[9];
            result.AgeCategory = tableCells[10];
            result.Squat = double.Parse(tableCells[11]);
            result.Bench = double.Parse(tableCells[12]);
            result.Deadlift = double.Parse(tableCells[13]);
            result.Total = double.Parse(tableCells[14]);
            result.WilksPoints = double.Parse(tableCells[15]);
            result.Year = tableCells[16];

            if (tableCells[17] == "yes")
                result.Unequipped = true;

            return result;
        }
    }
}