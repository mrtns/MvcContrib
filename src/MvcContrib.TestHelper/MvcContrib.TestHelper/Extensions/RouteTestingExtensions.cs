﻿using System;
using System.Linq.Expressions;
using System.Web;
using System.Web.Mvc;
using System.Web.Routing;
using Rhino.Mocks;
using System.Reflection;

namespace MvcContrib.TestHelper
{
    /// <summary>
    /// Used to simplify testing routes.
    /// </summary>
    public static class RouteTestingExtensions
    {
        private static HttpContextBase FakeHttpContext(string url)
        {
            var request = MockRepository.GenerateStub<HttpRequestBase>();            
            request.Stub(x => x.AppRelativeCurrentExecutionFilePath).Return(url).Repeat.Any();
            request.Stub(x => x.PathInfo).Return(string.Empty).Repeat.Any();

            var context = MockRepository.GenerateStub<HttpContextBase>();
            context.Stub(x => x.Request).Return(request).Repeat.Any();

            return context;        
        }

        /// <summary>
        /// A way to start the fluent interface and and which method to use
        /// since you have a method constraint in the route.
        /// </summary>
        /// <param name="url"></param>
        /// <param name="httpMethod"></param>
        /// <returns></returns>
        public static RouteData WithMethod(this string url, string httpMethod)
        {
            return Route(url, httpMethod);
        }

        public static RouteData WithMethod(this string url, HttpVerbs verb)
        {
            return WithMethod(url, verb.ToString("g"));
        }

        /// <summary>
        /// Find the route for a URL and an Http Method
        /// because you have a method contraint on the route 
        /// </summary>
        /// <param name="url"></param>
        /// <param name="httpMethod"></param>
        /// <returns></returns>
        public static RouteData Route(string url, string httpMethod)
        {
            var context = FakeHttpContext(url, httpMethod);
            return RouteTable.Routes.GetRouteData(context);
        }

        private static HttpContextBase FakeHttpContext(string url, string method)
        {
            var context = FakeHttpContext(url);
            context.Request.Stub(x => x.HttpMethod).Return(method).Repeat.Any();
            return context;
        }

        /// <summary>
        /// Returns the corresponding route for the URL.  Returns null if no route was found.
        /// </summary>
        /// <param name="url">The app relative url to test.</param>
        /// <returns>A matching <see cref="RouteData" />, or null.</returns>
        public static RouteData Route(this string url)
        {
            var context = FakeHttpContext(url);
            return RouteTable.Routes.GetRouteData(context);
        }

        /// <summary>
        /// Asserts that the route matches the expression specified.  Checks controller, action, and any method arguments
        /// into the action as route values.
        /// </summary>
        /// <typeparam name="TController">The controller.</typeparam>
        /// <param name="routeData">The routeData to check</param>
        /// <param name="action">The action to call on TController.</param>
        public static RouteData ShouldMapTo<TController>(this RouteData routeData, Expression<Func<TController, ActionResult>> action)
            where TController : Controller
        {            
            routeData.ShouldNotBeNull("The URL did not match any route");

            //check controller
            routeData.ShouldMapTo<TController>();
            
            //check action
            var methodCall = (MethodCallExpression) action.Body;
            string actualAction = routeData.Values.GetValue("action").ToString();
            string expectedAction = methodCall.Method.Name;
            actualAction.AssertSameStringAs(expectedAction);
                        
            //check parameters
            for (int i = 0; i < methodCall.Arguments.Count; i++)
            {
				ParameterInfo param = methodCall.Method.GetParameters()[i];
				bool isNullable = param.ParameterType.UnderlyingSystemType.IsGenericType && param.ParameterType.UnderlyingSystemType.GetGenericTypeDefinition() == typeof(Nullable<>);
				string name = param.Name;
				object expectedValue = routeData.Values.GetValue(name);
				object actualValue = null;
				Expression expressionToEvaluate = methodCall.Arguments[i];

				// If the parameter is nullable and the expression is a Convert UnaryExpression, 
				// we actually want to test against the value of the expression's operand.
				if (expressionToEvaluate.NodeType == ExpressionType.Convert
					&& expressionToEvaluate is UnaryExpression)
				{
					expressionToEvaluate = ((UnaryExpression)expressionToEvaluate).Operand;
				}

				switch (expressionToEvaluate.NodeType)
				{
					case ExpressionType.Constant:
						actualValue = ((ConstantExpression)expressionToEvaluate).Value;
						break;

					case ExpressionType.New:
					case ExpressionType.MemberAccess:
						actualValue = Expression.Lambda(expressionToEvaluate).Compile().DynamicInvoke();
						break;
				}

				if (isNullable && (string)expectedValue == String.Empty && actualValue == null)
				{
					// The parameter is nullable so an expected value of '' is equivalent to null;
					continue;
				}

				if (actualValue is DateTime)
				{
					expectedValue = Convert.ToDateTime(expectedValue);
				}
				else
				{
					actualValue = (actualValue == null ? actualValue : actualValue.ToString());
				}

				expectedValue.ShouldEqual(actualValue, 
					String.Format("Value for parameter '{0}' did not match: expected '{1}' but was '{2}'.", 
										name, expectedValue, actualValue));
			}

            return routeData;
        }

        /// <summary>
        /// Converts the URL to matching RouteData and verifies that it will match a route with the values specified by the expression.
        /// </summary>
        /// <typeparam name="TController">The type of controller</typeparam>
        /// <param name="relativeUrl">The ~/ based url</param>
        /// <param name="action">The expression that defines what action gets called (and with which parameters)</param>
        /// <returns></returns>
        public static RouteData ShouldMapTo<TController>(this string relativeUrl, Expression<Func<TController, ActionResult>> action) where TController : Controller
        {
            return relativeUrl.Route().ShouldMapTo(action);
        }

        /// <summary>
        /// Verifies the <see cref="RouteData">routeData</see> maps to the controller type specified.
        /// </summary>
        /// <typeparam name="TController"></typeparam>
        /// <param name="routeData"></param>
        /// <returns></returns>
        public static RouteData ShouldMapTo<TController>(this RouteData routeData) where TController : Controller
        {
            //strip out the word 'Controller' from the type
            string expected = typeof(TController).Name.Replace("Controller", "");

            //get the key (case insensitive)
            string actual = routeData.Values.GetValue("controller").ToString();

            actual.AssertSameStringAs(expected);
            return routeData;
        }

        /// <summary>
        /// Verifies the <see cref="RouteData">routeData</see> will instruct the routing engine to ignore the route.
        /// </summary>
        /// <param name="relativeUrl"></param>
        /// <returns></returns>
        public static RouteData ShouldBeIgnored(this string relativeUrl)
        {
            RouteData routeData = relativeUrl.Route();

            routeData.RouteHandler.ShouldBe<StopRoutingHandler>("Expected StopRoutingHandler, but wasn't");
            return routeData;
        }

        /// <summary>
        /// Gets a value from the <see cref="RouteValueDictionary" /> by key.  Does a
        /// case-insensitive search on the keys.
        /// </summary>
        /// <param name="routeValues"></param>
        /// <param name="key"></param>
        /// <returns></returns>
        public static object GetValue(this RouteValueDictionary routeValues, string key)
        {
            foreach(var routeValueKey in routeValues.Keys)
            {
                if(string.Equals(routeValueKey, key, StringComparison.InvariantCultureIgnoreCase))
                {
					if (routeValues[routeValueKey] == null)
						return null;
                	return routeValues[routeValueKey].ToString();
                }
            }

            return null;
        }
    }
}
