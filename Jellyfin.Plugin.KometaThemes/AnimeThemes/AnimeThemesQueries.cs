using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Jellyfin.Plugin.KometaThemes.AnimeThemes;

/// <summary>
/// The GraphQL documents sent to animethemes.moe.
/// </summary>
/// <remarks>
/// The REST API this plugin used before (<c>https://api.animethemes.moe/</c>) answers 503 on every
/// route since November 2026; the same data is served as GraphQL. Each document asks for exactly the
/// fields the models carry, so the mapping in <see cref="AnimeThemesMapper"/> never depends on a
/// field it did not request.
/// </remarks>
internal static class AnimeThemesQueries
{
    /// <summary>Every field of an anime the rest of the plugin reads: covers, titles, franchise, IDs, themes and files.</summary>
    private const string DetailFields = """
        fragment AnimeDetail on Anime {
          id
          slug
          year
          season
          format
          title {
            romaji
            english
            native
          }
          images(first: 20) {
            nodes {
              facet
              link
            }
          }
          synonyms {
            text
          }
          series(first: 10) {
            nodes {
              id
              name
              slug
            }
          }
          resources(first: 100) {
            nodes {
              externalId
              site
            }
          }
          animethemes(first: 200) {
            id
            type
            sequence
            slug
            song {
              id
              title {
                romaji
                native
              }
              performances(first: 50) {
                relevance
                artist {
                  id
                  name {
                    main
                  }
                  slug
                }
              }
            }
            animethemeentries(first: 100) {
              id
              version
              episodes
              nsfw
              spoiler
              videos(first: 100) {
                nodes {
                  id
                  basename
                  link
                  resolution
                  source
                  overlap
                  nc
                  subbed
                  lyrics
                  size
                  audio {
                    id
                    link
                    size
                  }
                }
              }
            }
          }
        }
        """;

    /// <summary>What a search result carries: covers, other titles, franchise and theme stubs, but no files.</summary>
    private const string SummaryFields = """
        fragment AnimeSummary on Anime {
          id
          slug
          year
          season
          format
          title {
            romaji
            english
            native
          }
          images(first: 20) {
            nodes {
              facet
              link
            }
          }
          synonyms {
            text
          }
          series(first: 10) {
            nodes {
              id
              name
              slug
            }
          }
          animethemes(first: 200) {
            id
            type
            sequence
            slug
          }
        }
        """;

    /// <summary>Looks anime up by their ID on another site.</summary>
    internal const string FindByExternalIds = """
        query($site: ResourceSite!, $ids: [Int!]) {
          findAnimeByExternalSite(site: $site, id: $ids) {
            ...AnimeDetail
          }
        }
        """ + DetailFields;

    /// <summary>What an anime of a franchise carries; its themes are read only for the entries a season takes.</summary>
    private const string MemberFields = """
        fragment AnimeMember on Anime {
          id
          slug
          year
          season
          format
          title {
            romaji
            english
            native
          }
        }
        """;

    /// <summary>Searches anime by title, synonyms included.</summary>
    internal const string Search = """
        query($search: String!) {
          search(search: $search, first: 15) {
            anime {
              ...AnimeSummary
            }
          }
        }
        """ + SummaryFields;

    /// <summary>Reads a franchise and the anime animethemes.moe groups into it.</summary>
    internal const string Series = """
        query($slug: String!) {
          series(slug: $slug) {
            id
            name
            slug
            anime(first: 200) {
              nodes {
                ...AnimeMember
              }
            }
          }
        }
        """ + MemberFields;

    /// <summary>
    /// One document with a query per ID, so a batch costs one request instead of one per entry.
    /// </summary>
    /// <param name="ids">Animethemes.moe IDs, in the order the aliases should carry them.</param>
    /// <returns>The GraphQL document.</returns>
    internal static string AnimeByIds(IReadOnlyList<int> ids)
    {
        ArgumentNullException.ThrowIfNull(ids);

        var declarations = string.Join(", ", ids.Select((_, index) => "$" + IdVariable(index) + ": Int!"));
        var aliases = string.Join(' ', ids.Select((_, index) => string.Create(
            CultureInfo.InvariantCulture,
            $"a{index}: animePagination(first: 1, id: ${IdVariable(index)}) {{ data {{ ...AnimeDetail }} }}")));
        return string.Create(CultureInfo.InvariantCulture, $"query({declarations}) {{ {aliases} }} ") + DetailFields;
    }

    /// <summary>
    /// The variables of <see cref="AnimeByIds"/>: one ID per alias.
    /// </summary>
    /// <param name="ids">Animethemes.moe IDs, in the same order.</param>
    /// <returns>Variable names and values.</returns>
    internal static Dictionary<string, object?> AnimeByIdVariables(IReadOnlyList<int> ids)
    {
        ArgumentNullException.ThrowIfNull(ids);

        var variables = new Dictionary<string, object?>(StringComparer.Ordinal);
        for (var index = 0; index < ids.Count; index++)
        {
            variables[IdVariable(index)] = ids[index];
        }

        return variables;
    }

    /// <summary>
    /// The name of the variable of one alias of <see cref="AnimeByIds"/>, without its dollar sign.
    /// </summary>
    /// <param name="index">Position of the ID in the batch.</param>
    /// <returns>The variable name, such as <c>id0</c>.</returns>
    private static string IdVariable(int index) => "id" + index.ToString(CultureInfo.InvariantCulture);
}
