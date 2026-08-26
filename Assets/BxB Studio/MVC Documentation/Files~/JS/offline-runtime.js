String.prototype.empty = function ()
{
	return !this || this.length === 0 || !this.trim();
};

Array.prototype.distinct = function ()
{
	var result = [];

	for (var i = 0; i < this.length; i++)
		if (result.indexOf(this[i]) < 0)
			result.push(this[i]);

	return result;
};

function DecodeURLFragment(fragment)
{
	try
	{
		return decodeURIComponent(fragment);
	}
	catch (error)
	{
		return fragment;
	}
}

function Timeout(milliseconds)
{
	return new Promise(function (resolve) { setTimeout(resolve, milliseconds); });
}

function ScrollToElement(id)
{
	var element = document.getElementById(DecodeURLFragment(id));

	if (element)
		element.scrollIntoView({ behavior: "smooth", block: "start" });
}

function GoToURL(url, scroll)
{
	if (typeof url !== "string" || url.length < 1)
		return;

	if (url.startsWith("#"))
	{
		if (location.hash !== url)
		{
			try
			{
				history.pushState(null, "", url);
			}
			catch (error)
			{
				location.hash = url;
			}
		}

		if (scroll !== false)
			requestAnimationFrame(function () { ScrollToElement(url.substring(1)); });

		return;
	}

	location.href = url;
}

function Filename(path)
{
	var cleanPath = String(path).split("#")[0].split("?")[0];
	var segments = cleanPath.split("/");

	return segments[segments.length - 1];
}

function ShowWindow(element, duration)
{
	$(element).fadeIn(duration || 0);
}

function HideWindow(element, duration)
{
	$(element).fadeOut(duration || 0);
}

function GetHostname()
{
	return "https://mvc.gg/";
}

function InternalErrorMessage()
{
	console.error("The requested online action is unavailable in the offline documentation.");
}

$(function ()
{
	$(document.body).addClass("ready");
});