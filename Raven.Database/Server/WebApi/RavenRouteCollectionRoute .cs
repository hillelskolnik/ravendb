﻿// -----------------------------------------------------------------------
//  <copyright file="RavenRouteCollectionRoute .cs" company="Hibernating Rhinos LTD">
//      Copyright (c) Hibernating Rhinos LTD. All rights reserved.
//  </copyright>
// -----------------------------------------------------------------------
using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Net.Http;
using System.Runtime.Caching;
using System.Text;
using System.Web.Http.Routing;

namespace Raven.Database.Server.WebApi
{
    internal class RavenRouteCollectionRoute : IHttpRoute, IReadOnlyCollection<IHttpRoute>
    {
        public const string SubRouteDataKey = "MS_SubRoutes";

        private static readonly IDictionary<string, object> Empty = new ReadOnlyDictionary<string, object>(new Dictionary<string, object>());

        private readonly IReadOnlyCollection<IHttpRoute> subRoutes;

        public RavenRouteCollectionRoute(IReadOnlyCollection<IHttpRoute> subRoutes)
        {
            this.subRoutes = subRoutes;
        }

        public IHttpRouteData GetRouteData(string virtualPathRoot, HttpRequestMessage request)
        {
            try
            {
                var route = new SubRoute(request.RequestUri.LocalPath);
                while (!route.AtEnd())
                {
                    var key = Tuple.Create(request.Method.Method, route);
                    IHttpRoute[] data;
                    if (routDataCache.TryGetValue(key, out data))
                    {
                        var httpRouteDatas = data.Select(x => x.GetRouteData(virtualPathRoot, request)).ToArray();
                        return new RavenRouteCollectionRouteData(this, httpRouteDatas);
                    }
                    route.NextSubRoute();
                }
                return LocateRouteData(virtualPathRoot, request);
            }
            catch (Exception e)
            {
                Console.WriteLine(e.Message);
                throw e;
            }
            
        }

        private RavenRouteCollectionRouteData LocateRouteData(string virtualPathRoot, HttpRequestMessage request)
        {
            var matches = (
                from route in subRoutes
                let match = route.GetRouteData(virtualPathRoot, request)
                where match != null
                select new {match, route}
                ).ToArray();

            var matchesTemplateRoutes = matches.Select(m => m.match.Route.RouteTemplate).Distinct();
            var result = matches.Length == 0 ? null : new RavenRouteCollectionRouteData(this, matches.Select(x=>x.match).ToArray());
            var routes = matches.Select(x => x.route).ToArray();
            foreach (var matchRoute in matchesTemplateRoutes)
            {
                var subRoute = new SubRoute(request.RequestUri.LocalPath);
                subRoute.ReduceRouteMatchTemplate(matchRoute);
                routDataCache.TryAdd(Tuple.Create(request.Method.Method, subRoute), routes);
            }
            return result;
        }

        private SubRoute GetSubRout(string localPath)
        {
            throw new NotImplementedException();
        }

        public IHttpVirtualPathData GetVirtualPath(HttpRequestMessage request, IDictionary<string, object> values)
        {
            return null;
        }

        public string RouteTemplate
        {
            get
            {
                return string.Empty;
            }
        }

        public IDictionary<string, object> Defaults
        {
            get
            {
                return Empty;
            }
        }

        public IDictionary<string, object> Constraints
        {
            get
            {
                return Empty;
            }
        }

        public IDictionary<string, object> DataTokens
        {
            get
            {
                return null;
            }
        }

        public HttpMessageHandler Handler
        {
            get
            {
                return null;
            }
        }

        public IEnumerator<IHttpRoute> GetEnumerator()
        {
            return subRoutes.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return subRoutes.GetEnumerator();
        }

        public int Count
        {
            get
            {
                return subRoutes.Count;
            }
        }

        private readonly ConcurrentDictionary<Tuple<string, SubRoute>, IHttpRoute[]> routDataCache = new ConcurrentDictionary<Tuple<string, SubRoute>, IHttpRoute[]>();
        private static readonly char[] RouteSpliter = new[] { '/' };

        private class SubRoute
        {
            private readonly string fullRoute;
            private int subRouteLength;
            public SubRoute(String fullRoute)
            {
                this.fullRoute = fullRoute;
                subRouteLength = fullRoute.Length;
            }

            public override string ToString()
            {
                return fullRoute.Substring(0, subRouteLength);
            }

            public void NextSubRoute()
            {
                subRouteLength = fullRoute.LastIndexOf('/', subRouteLength - 1, subRouteLength - 1);
            }

            public bool AtEnd()
            {
                return subRouteLength == -1;
            }
            public override bool Equals(object obj)
            {
                var subRoute = obj as SubRoute;
                if (subRoute == null) return false;
                if (subRoute.subRouteLength != subRouteLength) return false;
                if (IsOriginalRoute() != subRoute.IsOriginalRoute()) return false;
                return string.Compare(fullRoute, 0, subRoute.fullRoute, 0, subRouteLength) == 0;
            }

            public bool IsOriginalRoute()
            {
                return subRouteLength == fullRoute.Length;
            }
            public override int GetHashCode()
            {
                int code = 0;
                for (int index = 0; index < subRouteLength; index++)
                {
                    code = code * 397 ^ fullRoute[index];
                }
                if (IsOriginalRoute())
                    code = code * 397 ^ 7;
                return code;
            }

            public void ReduceRouteMatchTemplate(string templateRoute)
            {
                var lastIndexOfStar = templateRoute.LastIndexOf("/{*", StringComparison.InvariantCulture);
                if (lastIndexOfStar == -1) return;
                var lastIndexOfDash = Math.Max(templateRoute.LastIndexOf('/', lastIndexOfStar - 1, lastIndexOfStar - 1), 0);
                while (AtEnd() == false)
                {
                    var length = lastIndexOfStar - lastIndexOfDash;
                    var match = string.Compare(fullRoute, subRouteLength - length, templateRoute, lastIndexOfDash, length) == 0;
                    if (match && (IsOriginalRoute() || fullRoute[subRouteLength] == '/'))
                    {
                        break;
                    }
                    NextSubRoute();
                }
                if (AtEnd()) throw new IndexOutOfRangeException(String.Format("Could not match route template: {0}, with route: {1}, this should not happen!", templateRoute, fullRoute));
            }
        }

        private class RavenRouteCollectionRouteData : IHttpRouteData
        {
            public RavenRouteCollectionRouteData(IHttpRoute parent, IHttpRouteData[] subRouteDatas)
            {
                Route = parent;
                Values = new HttpRouteValueDictionary { { SubRouteDataKey, subRouteDatas } };
            }

            public IHttpRoute Route { get; private set; }

            public IDictionary<string, object> Values { get; private set; }
        }
    }
}