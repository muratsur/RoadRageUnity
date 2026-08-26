function RefreshTabsContainerMask()
{
	var tabsContainer = $("aside#docs-aside-nav > div.tabs-container");
	var shadowSize = 48;
	var top = Math.min(tabsContainer.scrollTop(), shadowSize);
	var bottom = Math.max(tabsContainer.scrollTop() + tabsContainer.outerHeight() - tabsContainer.prop("scrollHeight") + shadowSize, 0);
	var stretchMask = false;
	var mask = "linear-gradient(to bottom, transparent " + (stretchMask ? 0 : top - shadowSize) + "px, white " + top + "px, white calc(100% - " + (shadowSize - bottom) + "px), transparent calc(100% + " + (stretchMask ? 0 : bottom) + "px))";

	tabsContainer.css({
		"mask-image": mask,
		"-webkit-mask-image": mask,
		"-moz-mask-image": mask,
		"-ms-mask-image": mask,
		"-o-mask-image": mask
	});
}

function GetDocsHeadingText(element)
{
	var heading = element.clone();

	heading.find(".docs-badge").remove();

	return heading.text();
}

(async function ()
{
	let versionEnum = $("div#docs-version-enum.enumerator");
	let physicsEnum = $("div#docs-physics-enum.enumerator");

	versionEnum.unbind("change").change(async function ()
	{
		var options = versionEnum.find("div.options-container > div.option");
		var newVersionIndex = versionEnum.attr("data-value");
		var newVersion = $(options[newVersionIndex]).text();

		await Timeout(250);

		if (newVersion !== docsVersion)
			GoToURL(location.origin + location.pathname + (newVersionIndex > 0 ? "?version=" + newVersion : "") + location.hash);
	});
	physicsEnum.unbind("change").change(function () { });

	let sections = $("div#docs-sections-container > section");
	let searchContainer = $("aside#docs-aside-nav > div.search-container");
	let searchInput = searchContainer.find("input#docs-manual-search-input, input#docs-api-search-input");
	let tabsContainer = $("aside#docs-aside-nav > div.tabs-container");
	let images = sections.find("img[src]");
	let imagePreviewWindowContainer = $("div#image-preview-container.window-container");
	let paragraphs = $("section div.paragraph, section div.helpbox, section div.unity-helpbox, section div.docs-related, section li, section table td");

	function GetDocsFragmentTarget(fragment)
	{
		if (typeof fragment !== "string" || !fragment.startsWith("#") || fragment.length < 2)
			return null;

		return document.getElementById(DecodeURLFragment(fragment.substring(1)));
	}

	tabsContainer.find("div.tab").remove();
	sections.removeAttr("id");
	$(document).scroll(RefreshTabsContainerMask);
	sections.each((index, section) =>
	{
		section = $(section);

		if (section.attr("data-ignore"))
			return;

		var titleElement = section.find("div.title");
		var title = GetDocsHeadingText(titleElement);
		var id = title.toLowerCase().replace(/\s/g, "-").replace(/\(/g,"-").replace(/\)/g,"").replace(/&/g, "and").replace(/\./g, "-").replace(/,\s/g, "-").replace(/,/g, "-").replace(/\//g, "-").replace(/\\/g, "-");
		var tab = $("<div></div>").addClass("tab").attr("data-keywords", section.attr("data-keywords")).appendTo(tabsContainer);
		
		if (section.attr("data-hidden"))
			tab.attr("data-hidden", "true");

		var textElement = $("<div></div>").addClass("text").text(title).appendTo(tab);

		if (titleElement.hasClass("no-text-transform"))
			textElement.addClass("no-text-transform");

		titleElement.addClass("icon-link");

		if (index === 0)
			tab.addClass("active");

		var parentTab = null;
		var childParentTab = null;
		var childChildParentTab = null;
		var childChildChildParentTab = null;
		var childChildChildChildParentTab = null;
		var childChildChildChildChildParentTab = null;
		var childChildChildChildChildChildParentTab = null;
		var childChildChildChildChildChildChildParentTab = null;
		var childChildChildChildChildChildChildChildParentTab = null;
		var childChildChildChildChildChildChildChildChildParentTab = null;
		var parentSection = null;

		if (section.attr("data-level") === "1" || section.attr("data-level") === "2" || section.attr("data-level") === "3" || section.attr("data-level") === "4" || section.attr("data-level") === "5" || section.attr("data-level") === "6" || section.attr("data-level") === "7" || section.attr("data-level") === "8" || section.attr("data-level") === "9" || section.attr("data-level") === "10")
		{
			var parentIndex = index;

			while (parentIndex > 0 && ($(sections[parentIndex]).attr("data-level") === "1" || $(sections[parentIndex]).attr("data-level") === "2" || $(sections[parentIndex]).attr("data-level") === "3" || $(sections[parentIndex]).attr("data-level") === "4" || $(sections[parentIndex]).attr("data-level") === "5" || $(sections[parentIndex]).attr("data-level") === "6" || $(sections[parentIndex]).attr("data-level") === "7" || $(sections[parentIndex]).attr("data-level") === "8" || $(sections[parentIndex]).attr("data-level") === "9" || $(sections[parentIndex]).attr("data-level") === "10"))
				parentIndex--;

			parentTab = $(tabsContainer.find("div.tab")[parentIndex]);
			parentSection = $(sections[parentIndex]);
			tab.attr("data-parent", parentSection.attr("id"));
			
			if (section.attr("data-level") === "1")
			{
				tab.css("display", "none");

				id = parentSection.attr("id") + "-" + id;
			}
		}
		
		if (section.attr("data-level") === "2" || section.attr("data-level") === "3" || section.attr("data-level") === "4" || section.attr("data-level") === "5" || section.attr("data-level") === "6" || section.attr("data-level") === "7" || section.attr("data-level") === "8" || section.attr("data-level") === "9" || section.attr("data-level") === "10")
		{
			var parentIndex = index;

			while (parentIndex > 0 && ($(sections[parentIndex]).attr("data-level") === "2" || $(sections[parentIndex]).attr("data-level") === "3" || $(sections[parentIndex]).attr("data-level") === "4" || $(sections[parentIndex]).attr("data-level") === "5" || $(sections[parentIndex]).attr("data-level") === "6" || $(sections[parentIndex]).attr("data-level") === "7" || $(sections[parentIndex]).attr("data-level") === "8" || $(sections[parentIndex]).attr("data-level") === "9" || $(sections[parentIndex]).attr("data-level") === "10"))
				parentIndex--;

			childParentTab = $(tabsContainer.find("div.tab")[parentIndex]);
			parentSection = $(sections[parentIndex]);
			tab.attr("data-child-parent", parentSection.attr("id"));
			
			if (section.attr("data-level") === "2")
			{
				tab.css("display", "none");
				
				id = parentSection.attr("id") + "-" + id;
			}
		}
		
		if (section.attr("data-level") === "3" || section.attr("data-level") === "4" || section.attr("data-level") === "5" || section.attr("data-level") === "6" || section.attr("data-level") === "7" || section.attr("data-level") === "8" || section.attr("data-level") === "9" || section.attr("data-level") === "10")
		{
			var parentIndex = index;

			while (parentIndex > 0 && ($(sections[parentIndex]).attr("data-level") === "3" || $(sections[parentIndex]).attr("data-level") === "4" || $(sections[parentIndex]).attr("data-level") === "5" || $(sections[parentIndex]).attr("data-level") === "6" || $(sections[parentIndex]).attr("data-level") === "7" || $(sections[parentIndex]).attr("data-level") === "8" || $(sections[parentIndex]).attr("data-level") === "9" || $(sections[parentIndex]).attr("data-level") === "10"))
				parentIndex--;

			childChildParentTab = $(tabsContainer.find("div.tab")[parentIndex]);
			parentSection = $(sections[parentIndex]);
			tab.attr("data-child-child-parent", parentSection.attr("id"));
			
			if (section.attr("data-level") === "3")
			{
				tab.css("display", "none");
				
				id = parentSection.attr("id") + "-" + id;
			}
		}
		
		if (section.attr("data-level") === "4" || section.attr("data-level") === "5" || section.attr("data-level") === "6" || section.attr("data-level") === "7" || section.attr("data-level") === "8" || section.attr("data-level") === "9" || section.attr("data-level") === "10")
		{
			var parentIndex = index;

			while (parentIndex > 0 && ($(sections[parentIndex]).attr("data-level") === "4" || $(sections[parentIndex]).attr("data-level") === "5" || $(sections[parentIndex]).attr("data-level") === "6" || $(sections[parentIndex]).attr("data-level") === "7" || $(sections[parentIndex]).attr("data-level") === "8" || $(sections[parentIndex]).attr("data-level") === "9" || $(sections[parentIndex]).attr("data-level") === "10"))
				parentIndex--;

			childChildChildParentTab = $(tabsContainer.find("div.tab")[parentIndex]);
			parentSection = $(sections[parentIndex]);
			tab.attr("data-child-child-child-parent", parentSection.attr("id"));

			if (section.attr("data-level") === "4")
			{
				tab.css("display", "none");

				id = parentSection.attr("id") + "-" + id;
			}
		}

		if (section.attr("data-level") === "5" || section.attr("data-level") === "6" || section.attr("data-level") === "7" || section.attr("data-level") === "8" || section.attr("data-level") === "9" || section.attr("data-level") === "10")
		{
			var parentIndex = index;

			while (parentIndex > 0 && ($(sections[parentIndex]).attr("data-level") === "5" || $(sections[parentIndex]).attr("data-level") === "6" || $(sections[parentIndex]).attr("data-level") === "7" || $(sections[parentIndex]).attr("data-level") === "8" || $(sections[parentIndex]).attr("data-level") === "9" || $(sections[parentIndex]).attr("data-level") === "10"))
				parentIndex--;

			childChildChildChildParentTab = $(tabsContainer.find("div.tab")[parentIndex]);
			parentSection = $(sections[parentIndex]);
			tab.attr("data-child-child-child-child-parent", parentSection.attr("id"));

			if (section.attr("data-level") === "5")
			{
				tab.css("display", "none");

				id = parentSection.attr("id") + "-" + id;
			}
		}

		if (section.attr("data-level") === "6" || section.attr("data-level") === "7" || section.attr("data-level") === "8" || section.attr("data-level") === "9" || section.attr("data-level") === "10")
		{
			var parentIndex = index;

			while (parentIndex > 0 && ($(sections[parentIndex]).attr("data-level") === "6" || $(sections[parentIndex]).attr("data-level") === "7" || $(sections[parentIndex]).attr("data-level") === "8" || $(sections[parentIndex]).attr("data-level") === "9" || $(sections[parentIndex]).attr("data-level") === "10"))
				parentIndex--;

			childChildChildChildChildParentTab = $(tabsContainer.find("div.tab")[parentIndex]);
			parentSection = $(sections[parentIndex]);
			tab.attr("data-child-child-child-child-child-parent", parentSection.attr("id"));

			if (section.attr("data-level") === "6")
			{
				tab.css("display", "none");

				id = parentSection.attr("id") + "-" + id;
			}
		}

		if (section.attr("data-level") === "7" || section.attr("data-level") === "8" || section.attr("data-level") === "9" || section.attr("data-level") === "10")
		{
			var parentIndex = index;

			while (parentIndex > 0 && ($(sections[parentIndex]).attr("data-level") === "7" || $(sections[parentIndex]).attr("data-level") === "8" || $(sections[parentIndex]).attr("data-level") === "9" || $(sections[parentIndex]).attr("data-level") === "10"))
				parentIndex--;

			childChildChildChildChildChildParentTab = $(tabsContainer.find("div.tab")[parentIndex]);
			parentSection = $(sections[parentIndex]);
			tab.attr("data-child-child-child-child-child-child-parent", parentSection.attr("id"));

			if (section.attr("data-level") === "7")
			{
				tab.css("display", "none");

				id = parentSection.attr("id") + "-" + id;
			}
		}

		if (section.attr("data-level") === "8" || section.attr("data-level") === "9" || section.attr("data-level") === "10")
		{
			var parentIndex = index;

			while (parentIndex > 0 && ($(sections[parentIndex]).attr("data-level") === "8" || $(sections[parentIndex]).attr("data-level") === "9" || $(sections[parentIndex]).attr("data-level") === "10"))
				parentIndex--;

			childChildChildChildChildChildChildParentTab = $(tabsContainer.find("div.tab")[parentIndex]);
			parentSection = $(sections[parentIndex]);
			tab.attr("data-child-child-child-child-child-child-child-parent", parentSection.attr("id"));

			if (section.attr("data-level") === "8")
			{
				tab.css("display", "none");

				id = parentSection.attr("id") + "-" + id;
			}
		}

		if (section.attr("data-level") === "9" || section.attr("data-level") === "10")
		{
			var parentIndex = index;

			while (parentIndex > 0 && ($(sections[parentIndex]).attr("data-level") === "9" || $(sections[parentIndex]).attr("data-level") === "10"))
				parentIndex--;

			childChildChildChildChildChildChildChildParentTab = $(tabsContainer.find("div.tab")[parentIndex]);
			parentSection = $(sections[parentIndex]);
			tab.attr("data-child-child-child-child-child-child-child-child-parent", parentSection.attr("id"));

			if (section.attr("data-level") === "9")
			{
				tab.css("display", "none");

				id = parentSection.attr("id") + "-" + id;
			}
		}

		if (section.attr("data-level") === "10")
		{
			var parentIndex = index;

			while (parentIndex > 0 && $(sections[parentIndex]).attr("data-level") === "10")
				parentIndex--;

			childChildChildChildChildChildChildChildChildParentTab = $(tabsContainer.find("div.tab")[parentIndex]);
			parentSection = $(sections[parentIndex]);
			tab.attr("data-child-child-child-child-child-child-child-child-child-parent", parentSection.attr("id"));
			tab.css("display", "none");

			id = parentSection.attr("id") + "-" + id;
		}

		/*var idSplit = id.split("-");

		id = [];

		for (var i = 0; i < idSplit.length; i++)
			id.push(idSplit[i]);

		id = id.join("-");*/
		
		tab.attr("data-id", id).unbind("click").click(function ()
		{
			if ($("body").hasClass("searching"))
				searchInput.val("").change();

			tabsContainer.find("div.tab").removeClass("active");
			tab.addClass("active");

			// Virtual scrolling: ensure target section is visible before navigating
			if (typeof window.vsNavigateToSection === "function")
				window.vsNavigateToSection(section[0]);

			GoToURL("#" + id);
		});
		section.attr("id", id);
		titleElement.unbind("click").click(function ()
		{
			GoToURL("#" + id);
		});
		section.find("div.sub-title").each(function (index, subTitle)
		{
			subTitle = $(subTitle);
			
			var subTitleID = GetDocsHeadingText(subTitle).toLowerCase().replace(/\s/g, "-").replace(/\(/g,"-").replace(/\)/g,"").replace(/&/g, "and").replace(/\./g, "-").replace(/,\s/g, "-").replace(/,/g, "-").replace(/\//g, "-").replace(/\\/g, "-");
			var subID = id + "-" + subTitleID;

			subTitle.addClass("icon-link");
			subTitle.attr("id", subID);
			subTitle.unbind("click").click(function ()
			{
				GoToURL("#" + subID);
			});
		});

		var tabsTimer = null;
		var scrollTimer = null;

		$(document).scroll(function ()
		{
			if (tabsTimer !== null)
				clearTimeout(tabsTimer);
				
			if ($("body").hasClass("searching"))
				return;

			var isSectionWithinWindowBounds = $(document).scrollTop() >= (section.prop("offsetTop") * $("html").css("zoom")) - $(window).height() / 3.5 && ($(document).scrollTop() < (section.prop("offsetTop") + section.height()) * $("html").css("zoom") - $(window).height() / 3.5 || index >= sections.not("[data-ignore=true]").length - 1);

			tabsTimer = setTimeout(function ()
			{
				if (isSectionWithinWindowBounds)
				{
					tabsContainer.find("div.tab").removeClass("parent").removeClass("active");
					tab.addClass("active");
					tabsContainer.find("div[data-parent].tab, div[data-child-parent].tab").css("display", "none");
					tab.css("display", "block");

					if (parentTab)
					{
						parentTab.addClass("parent");
						tabsContainer.find("div.tab[data-parent=" + tab.attr("data-parent") + "]").not("[data-child-parent]").css("display", "block");

						if (childParentTab)
						{
							childParentTab.addClass("parent");
							tabsContainer.find("div.tab[data-child-parent=" + tab.attr("data-child-parent") + "]").not("[data-child-child-parent]").css("display", "block");

							if (childChildParentTab)
							{
								childChildParentTab.addClass("parent");
								tabsContainer.find("div.tab[data-child-child-parent=" + tab.attr("data-child-child-parent") + "]").not("[data-child-child-child-parent]").css("display", "block");

								if (childChildChildParentTab)
								{
									childChildChildParentTab.addClass("parent");
									tabsContainer.find("div.tab[data-child-child-child-parent=" + tab.attr("data-child-child-child-parent") + "]").not("[data-child-child-child-child-parent]").css("display", "block");

									if (childChildChildChildParentTab)
									{
										childChildChildChildParentTab.addClass("parent");
										tabsContainer.find("div.tab[data-child-child-child-child-parent=" + tab.attr("data-child-child-child-child-parent") + "]").not("[data-child-child-child-child-child-parent]").css("display", "block");

										if (childChildChildChildChildParentTab)
										{
											childChildChildChildChildParentTab.addClass("parent");
											tabsContainer.find("div.tab[data-child-child-child-child-child-parent=" + tab.attr("data-child-child-child-child-child-parent") + "]").not("[data-child-child-child-child-child-child-parent]").css("display", "block");

											if (childChildChildChildChildChildParentTab)
											{
												childChildChildChildChildChildParentTab.addClass("parent");
												tabsContainer.find("div.tab[data-child-child-child-child-child-child-parent=" + tab.attr("data-child-child-child-child-child-child-parent") + "]").not("[data-child-child-child-child-child-child-child-parent]").css("display", "block");

												if (childChildChildChildChildChildChildParentTab)
												{
													childChildChildChildChildChildChildParentTab.addClass("parent");
													tabsContainer.find("div.tab[data-child-child-child-child-child-child-child-parent=" + tab.attr("data-child-child-child-child-child-child-child-parent") + "]").not("[data-child-child-child-child-child-child-child-child-parent]").css("display", "block");

													if (childChildChildChildChildChildChildChildParentTab)
													{
														childChildChildChildChildChildChildChildParentTab.addClass("parent");
														tabsContainer.find("div.tab[data-child-child-child-child-child-child-child-child-parent=" + tab.attr("data-child-child-child-child-child-child-child-child-parent") + "]").not("[data-child-child-child-child-child-child-child-child-child-parent]").css("display", "block");

														if (childChildChildChildChildChildChildChildChildParentTab)
														{
															childChildChildChildChildChildChildChildChildParentTab.addClass("parent");
															tabsContainer.find("div.tab[data-child-child-child-child-child-child-child-child-child-parent=" + tab.attr("data-child-child-child-child-child-child-child-child-child-parent") + "]").css("display", "block");
														}
														else
															tabsContainer.find("div.tab[data-child-child-child-child-child-child-child-child-parent=" + tab.attr("data-id") + "]").not("[data-child-child-child-child-child-child-child-child-child-parent]").css("display", "block");
													}
													else
														tabsContainer.find("div.tab[data-child-child-child-child-child-child-child-parent=" + tab.attr("data-id") + "]").not("[data-child-child-child-child-child-child-child-child-parent]").css("display", "block");
												}
												else
													tabsContainer.find("div.tab[data-child-child-child-child-child-child-parent=" + tab.attr("data-id") + "]").not("[data-child-child-child-child-child-child-child-parent]").css("display", "block");
											}
											else
												tabsContainer.find("div.tab[data-child-child-child-child-child-parent=" + tab.attr("data-id") + "]").not("[data-child-child-child-child-child-child-parent]").css("display", "block");
										}
										else
											tabsContainer.find("div.tab[data-child-child-child-child-parent=" + tab.attr("data-id") + "]").not("[data-child-child-child-child-child-parent]").css("display", "block");
									}
									else
										tabsContainer.find("div.tab[data-child-child-child-parent=" + tab.attr("data-id") + "]").not("[data-child-child-child-child-parent]").css("display", "block");
								}
								else
									tabsContainer.find("div.tab[data-child-child-parent=" + tab.attr("data-id") + "]").not("[data-child-child-child-parent]").css("display", "block");
							}
							else
								tabsContainer.find("div.tab[data-child-child-parent=" + tab.attr("data-id") + "]").not("[data-child-child-child-parent]").css("display", "block");
						}
						else
							tabsContainer.find("div.tab[data-child-parent=" + tab.attr("data-id") + "]").not("[data-child-child-parent]").css("display", "block");
					}
					else
						tabsContainer.find("div.tab[data-parent=" + tab.attr("data-id") + "]").not("[data-child-parent]").css("display", "block");

					RefreshTabsContainerMask();
				}
			}, 100);

			if (scrollTimer !== null)
				clearTimeout(scrollTimer);

			scrollTimer = setTimeout(function ()
			{
				if (isSectionWithinWindowBounds)
				{
					var scrollTop = (tab.prop("offsetTop") + (tab.height() * .5) - (tabsContainer.height() * .5)) * $("html").css("zoom");

					tabsContainer[0].scrollTo({
						top: scrollTop,
						left: 0,
						behavior: "smooth"
					});
				}
			}, 500);
		});

		if (section.hasClass("data-hidden"))
			return;

		if (window.docsOffline)
			return;

		var feedbackContainer = $("<div></div>").addClass("feedback-container").appendTo(section);
		var feedbackMessage = $("<div></div>").addClass("feedback-message").text("Was this section helpful?").appendTo(feedbackContainer);
		var feedbackActionsContainer = $("<div></div>").addClass("feedback-actions").appendTo(feedbackContainer);
		var feedbackTextFieldContainer = $("<div></div>").addClass("feedback-text-field-container").appendTo(feedbackContainer);
		var feedbackID = 0;

		$("<div></div>").addClass("feedback-action").addClass("link").addClass("no-text-decoration").text("Yes").appendTo(feedbackActionsContainer).click(function ()
		{
			feedbackActionsContainer.css("display", "none");
			feedbackMessage.html("Sending feedback...");
			$.ajax({
				type: "POST",
				url: location.href,
				data:
				{
					feedback: "yes",
					manual: id
				},
				dataType: "json",
				success: function (response)
				{
					//console.log(response);
		
					//response = JSON.parse(response);
		
					feedbackMessage.html("Thank you for helping improve Multiversal Vehicle Controller's documentation.<br />If you need help or have any questions, please consider <a aria-title=\"\" href=\"" + GetHostname() + "contact\" target=\"_blank\">contacting support</a>.");
				},
				error: function (jqXHR, status, error)
				{
					console.error(jqXHR, status, error);
					InternalErrorMessage();
				}
			});
		});
		$("<div></div>").addClass("feedback-action").addClass("link").addClass("no-text-decoration").text("No").appendTo(feedbackActionsContainer).click(function ()
		{
			feedbackActionsContainer.css("display", "none");
			feedbackMessage.html("We're sorry to hear that! Please help us improve Multiversal Vehicle Controller's documentation by providing a feedback.");
			feedbackTextFieldContainer.css("display", "block");
			$.ajax({
				type: "POST",
				url: location.href,
				data:
				{
					feedback: "no",
					manual: id
				},
				dataType: "json",
				success: function (response)
				{
					//console.log(response);
		
					//response = JSON.parse(response);
		
					if (response.code !== "200")
					{
						feedbackTextFieldContainer.css("display", "none");
						InternalErrorMessage();

						return;
					}

					feedbackID = response.feedback_id;
				},
				error: function (jqXHR, status, error)
				{
					console.error(jqXHR, status, error);
					InternalErrorMessage();
				}
			});
		});

		var feedbackTextFieldInputContainer = $("<div></div>").addClass("input-container").addClass("no-side-margin").appendTo(feedbackTextFieldContainer);
		var feedbackTextField = $("<input />").attr("placeholder", "How could documentation be better?").addClass("feedback-text-field").appendTo(feedbackTextFieldInputContainer);

		$("<button></button>").addClass("feedback-submit-button").addClass("no-margin").text("Send").appendTo(feedbackTextFieldContainer).click(function ()
		{
			var message = feedbackTextField.val();

			$(this).addClass("loading");
			feedbackTextField.attr("disabled", "");
			$.ajax({
				type: "POST",
				url: location.href,
				data:
				{
					feedback_id: feedbackID,
					feedback: message
				},
				dataType: "json",
				success: function (response)
				{
					//console.log(response);
		
					//response = JSON.parse(response);

					if (response.code !== 200)
					{
						InternalErrorMessage();

						return;
					}
		
					feedbackTextFieldContainer.css("display", "");
					feedbackMessage.html("Thank you for helping improve Multiversal Vehicle Controller's documentation.<br />If you need help or have any questions, please consider <a aria-title=\"\" href=\"" + GetHostname() + "contact\" target=\"_blank\">contacting support</a>.");
				},
				error: function (jqXHR, status, error)
				{
					console.error(jqXHR, status, error);
					InternalErrorMessage();
				}
			});
		});
	});
	images.each(index =>
	{
		var image = $(images[index]);
		var orgHeight = image.height();
		var orgWidth = image.width();

		image.attr("tabindex", "0");

		image.click(async function ()
		{
			ShowWindow(imagePreviewWindowContainer, 500);
			imagePreviewWindowContainer.addClass("spin-before");
			imagePreviewWindowContainer.addClass("icon-spin6");

			var src = image.attr("src");

			imagePreviewWindowContainer.find("div.window").css("background-image", "url(" + src + ")");
			imagePreviewWindowContainer.find("div.window").css("height", Math.min(orgHeight, $(window).height()));
			imagePreviewWindowContainer.find("div.window").css("width", Math.min(orgWidth, $(window).width()));
		});
	});
	paragraphs.each(index =>
	{
		var paragraph = $(paragraphs[index]);
		var html = paragraph.html();

		// quotes
		var quotePairs = [];

		for (var i = 0; i < html.length; i++)
		{
			var index1 = html.indexOf('`', i);

			if (index1 < 0)
				continue;
			
			var index2 = html.indexOf('`', index1 + 1);
				
			if (index2 < 0)
				continue;

			quotePairs.push([index1, index2]);

			i = index2 + 1;
		}

		for (var i = quotePairs.length - 1; i >= 0; i--)
			html = html.substring(0, quotePairs[i][0]) + '<span class="quote">' + html.substring(quotePairs[i][0] + 1, quotePairs[i][1]) + '</span>' + html.substring(quotePairs[i][1] + 1);

		// links
		var linkPairs = [];

		for (var i = 0; i < html.length; i++)
		{
			var middleIndex = html.indexOf("](", i);

			if (middleIndex < 0)
				break;

			var startIndex = html.lastIndexOf("[", middleIndex - 1);

			if (startIndex < 0)
				continue;

			var endIndex = html.indexOf(")", middleIndex + 2);

			if (endIndex < 0)
				continue;

			linkPairs.push([startIndex, middleIndex, endIndex]);

			i = endIndex + 1;
		}

		for (var i = linkPairs.length - 1; i >= 0; i--)
		{
			var linkPair = linkPairs[i];
			var link = html.substring(linkPair[0] + 1, linkPair[1]);
			var text = html.substring(linkPair[1] + 2, linkPair[2]);
			var isInternalLink = link.startsWith("#");
			var isLocalDocsLink = /^(?:Documentation|API)\.html(?:#.*)?$/i.test(link);
			var isLinkMissing = link.length < 1 || (isInternalLink && !GetDocsFragmentTarget(link));
			var isExternalLink = !isLinkMissing && !isInternalLink && !isLocalDocsLink;
			var isDownloadLink = !isLinkMissing && link.endsWith('.fbx');
			var isManualLink = !isLinkMissing && (link.startsWith("manual") || /^Documentation\.html/i.test(link));
			var isAPILink = !isLinkMissing && (link.startsWith("api") || /^API\.html/i.test(link));

			html = html.substring(0, linkPair[0]) + `<a aria-title="${text}"${isLinkMissing ? ' class="missing"' : ` href="${link}"`}${!isLinkMissing && isExternalLink ? ' target="_blank"' : ''}${isExternalLink || isLinkMissing ? ` data-tooltip="${isLinkMissing ? 'Link is missing' : isDownloadLink ? `Download '${Filename(link)}'` : 'External link'}"` : ''}>${text}${isDownloadLink || isExternalLink || isLocalDocsLink ? ` <i class="icon-${isDownloadLink ? 'download' : isManualLink ? 'book' : isAPILink ? 'file-code' : 'link-ext-alt'}"></i>` : ''}</a>` + html.substring(linkPair[2] + 1);
		}

		paragraph.html(html);
	});
	$(document).scroll(RefreshTabsContainerMask);
	tabsContainer.unbind("scroll").scroll(RefreshTabsContainerMask);
	tabsContainer.unbind("mouseover").mouseover(function ()
	{
		$(document.body).css("overflow", "hidden");
	});
	tabsContainer.unbind("mouseout").mouseout(function ()
	{
		$(document.body).css("overflow", "");
	});
	searchInput.change(function ()
	{
		var query = $(this).val().trim();

		if (query.empty())
		{
			$("body").removeClass("searching");
			tabsContainer.find("div.tab").removeClass("hide-tab");
			$(document).scroll();

			return;
		}

		$("body").addClass("searching");
		tabsContainer.find("div.tab").removeClass("active").removeClass("parent").each(function (i, tab)
		{
			let keywords = $(tab).attr("data-keywords").trim().split(" ").distinct();
			let keywordFound = true;
			let queries = query.split(" ").distinct();

			for (var j = 0; j < queries.length; j++)
			{
				let qFound = false;
				let q = queries[j].toUpperCase();

				for (var k = 0; k < keywords.length; k++)
				{
					let key = keywords[k].toUpperCase();

					if (key.includes(q))
						qFound = true;
				}

				if (!qFound)
					keywordFound = false;
			}

			if (keywordFound)
				$(tab).removeClass("hide-tab");
			else
				$(tab).addClass("hide-tab");
		});
		RefreshTabsContainerMask();
	}).keyup(() => { searchInput.change() });
	imagePreviewWindowContainer.click(function (e)
	{
		if (e.target !== this)
			return;

		HideWindow($(this), 500);
	});

	// ========== Virtual Scrolling ==========
	const vsBufferSize = 5;
	const vsThrottleDelay = 100;
	let vsLastUpdate = 0;
	let vsUpdateScheduled = false;

	// Get sections to virtualize (exclude data-hidden="true" sections)
	let vsSections = sections.filter(function() {
		return !$(this).attr("data-hidden");
	});

	// Track which sections are currently visible
	let vsVisibleIndices = new Set();

	// Initialize: measure all section heights and hide them
	function vsInitialize()
	{
		vsSections.each(function(index) {
			let section = $(this);

			// Ensure section is visible for measuring
			section.removeClass("vs-hidden");

			// Measure and store height
			let height = section.height();
			section.css("min-height", height + "px");

			// Hide the section
			section.addClass("vs-hidden");
		});

		// Show initial sections (first 6: index 0-5)
		vsShowRange(0, vsBufferSize);
	}

	// Show sections in a range and hide others
	function vsShowRange(centerIndex, buffer)
	{
		let startIndex = Math.max(0, centerIndex - buffer);
		let endIndex = Math.min(vsSections.length - 1, centerIndex + buffer);

		// Determine which sections to show and hide
		let newVisibleIndices = new Set();

		for (let i = startIndex; i <= endIndex; i++)
			newVisibleIndices.add(i);

		// Hide sections that are no longer in range
		vsVisibleIndices.forEach(function(index) {
			if (!newVisibleIndices.has(index))
			{
				let section = $(vsSections[index]);
				section.addClass("vs-hidden");
			}
		});

		// Show sections that are now in range
		newVisibleIndices.forEach(function(index) {
			if (!vsVisibleIndices.has(index))
			{
				let section = $(vsSections[index]);
				section.removeClass("vs-hidden");
			}
		});

		vsVisibleIndices = newVisibleIndices;
	}

	// Throttled update function
	function vsThrottledUpdate(centerIndex)
	{
		let now = Date.now();

		if (now - vsLastUpdate >= vsThrottleDelay)
		{
			vsLastUpdate = now;
			vsShowRange(centerIndex, vsBufferSize);
		}
		else if (!vsUpdateScheduled)
		{
			vsUpdateScheduled = true;

			setTimeout(function() {
				vsUpdateScheduled = false;
				vsLastUpdate = Date.now();
				vsShowRange(centerIndex, vsBufferSize);
			}, vsThrottleDelay - (now - vsLastUpdate));
		}
	}

	// Find the center section index based on scroll position
	function vsGetCenterSectionIndex()
	{
		let scrollTop = $(document).scrollTop();
		let viewportCenter = scrollTop + $(window).height() / 2;
		let centerIndex = 0;

		vsSections.each(function(index) {
			let section = $(this);
			let sectionTop = section.offset().top;
			let sectionBottom = sectionTop + parseFloat(section.css("min-height"));

			if (viewportCenter >= sectionTop && viewportCenter < sectionBottom)
			{
				centerIndex = index;
				return false; // break
			}
			else if (sectionTop > viewportCenter)
			{
				centerIndex = Math.max(0, index - 1);
				return false; // break
			}

			centerIndex = index;
		});

		return centerIndex;
	}

	// Set up IntersectionObserver
	let vsObserver = new IntersectionObserver(function(entries) {
		entries.forEach(function(entry) {
			if (entry.isIntersecting)
			{
				let centerIndex = vsGetCenterSectionIndex();
				vsThrottledUpdate(centerIndex);
			}
		});
	}, {
		root: null,
		rootMargin: "200px 0px",
		threshold: 0.1
	});

	// Observe all virtualized sections
	vsSections.each(function() {
		vsObserver.observe(this);
	});

	// Also update on scroll for edge cases
	$(document).on("scroll.virtualScroll", function() {
		let centerIndex = vsGetCenterSectionIndex();
		vsThrottledUpdate(centerIndex);
	});

	// Navigate to section by index (used by tab clicks)
	function vsNavigateToSection(sectionElement)
	{
		let section = $(sectionElement).closest("section");
		let index = vsSections.index(section[0]);

		if (index >= 0)
		{
			vsShowRange(index, vsBufferSize);

			return true;
		}

		return false;
	}

	// Recalculate heights for visible sections on resize
	$(window).on("resize.virtualScroll", function() {
		vsVisibleIndices.forEach(function(index) {
			let section = $(vsSections[index]);

			// Temporarily clear min-height to get natural height
			section.css("min-height", "");

			// Measure and restore
			let height = section.outerHeight(true);
			section.css("min-height", height + "px");
		});
	});

	// Initialize virtual scrolling
	vsInitialize();

	// Reveal virtualized sections before navigating generated in-page links.
	$("div#docs-sections-container a[href^='#']").off("click.docs-navigation").on("click.docs-navigation", function (e) {
		let href = $(this).attr("href");
		let target = GetDocsFragmentTarget(href);

		e.preventDefault();

		if (!target || !vsNavigateToSection(target))
			return;

		GoToURL(href);
	});

	// Handle initial page load with URL hash
	if (location.hash)
	{
		let target = GetDocsFragmentTarget(location.hash);

		if (target && vsNavigateToSection(target))
			requestAnimationFrame(function () { ScrollToElement(target.id); });
	}

	// Expose navigation function for tab clicks
	window.vsNavigateToSection = vsNavigateToSection;
	// ========== End Virtual Scrolling ==========

	$("div#page-loader.progress-bar").fadeOut(500);

	await Timeout(500);

	$("div#page-loader.progress-bar").css("display", "none");
	$(document).scroll();
})();
