var siteApi = (function ($)
{
	var modalOptions = { overlayOpacity: 0.3, closeOnOverlayClick: true };
	$.tablesorter.themes.bootstrap = {
		// these classes are added to the table. To see other table classes available,
		// look here: http://getbootstrap.com/css/#tables
		table: 'table table-bordered table-striped',
		caption: 'caption',
		// header class names
		header: 'bootstrap-header', // give the header a gradient background (theme.bootstrap_2.css)
		sortNone: '',
		sortAsc: '',
		sortDesc: '',
		active: '', // applied when column is sorted
		hover: '', // custom css required - a defined bootstrap style may not override other classes
		// icon class names
		icons: '', // add "bootstrap-icon-white" to make them white; this icon class is added to the <i> in the header
		iconSortNone: 'bootstrap-icon-unsorted', // class name added to icon when column is not sorted
		iconSortAsc: 'glyphicon glyphicon-chevron-up', // class name added to icon when column has ascending sort
		iconSortDesc: 'glyphicon glyphicon-chevron-down', // class name added to icon when column has descending sort
		filterRow: '', // filter row class; use widgetOptions.filter_cssFilter for the input/select element
		footerRow: '',
		footerCells: '',
		even: '', // even row zebra striping
		odd: ''  // odd row zebra striping
	};

	var statsLoadStarted = false;
	var statsTableEditor = null;
	var statsResponse = null;
	var tableDef = [
		{ name: "CPU Model", field: "CpuModel" },
		{ name: "MHz", field: "CpuMHz" },
		{ name: "CPU Usage (Overall)", field: "CpuUsage", type: "custom", customRender: RenderPercent },
		{ name: "CPU Usage (BlueIris)", field: "BiCpuUsage", type: "custom", customRender: RenderPercent },
		{ name: "MP/s", field: "Total_MPPS", type: "custom", customRender: RenderDec1 },
		{ name: "Cameras", field: "CameraCount" },
		{ name: "Memory (BlueIris)", field: "BiMemUsageMB", type: "custom", customRender: RenderMBtoMiB },
		{ name: "Memory Free / Total", field: "MemFreeMB", type: "custom", customRender: RenderMEM },
		{ name: "RAM Channels / MHz", field: "RamChannels", type: "custom", customRender: RenderRAM },
		{ name: "HW Video Accel", field: "HwAccel" },
		{ name: "Age", field: "age", type: "custom", customRender: RenderAge }
	];
	if (isAdmin)
		tableDef.push({ name: "Secret", field: "secret", type: "custom", customRender: RenderSecret });
	var tableOptions = {
		idColumn: "ID"
		, loadingImageUrl: "img/ajax-loader.gif"
		, theme: "bootstrap"
		, tableClass: "table"
		, customRowClick: statsRowClick
	}

	$('.nav-pills').stickyTabs({ hashChangeCallback: hashChangeCallback });
	function hashChangeCallback(hash)
	{
		if (hash == "#stats")
		{
			if (statsLoadStarted)
				return;
			statsLoadStarted = true;
			statsTableEditor = $("#DataTableWrapper").TableEditor(tableDef, tableOptions);
			ExecAPI("getUsageRecords", function (response)
			{
				statsResponse = response;
				if (response.result == "success")
				{
					if (response.secrets)
					{
						response.secretsMap = {};
						for (var i = 0; i < statsResponse.secrets.length; i++)
							response.secretsMap[statsResponse.secrets[i].ID] = statsResponse.secrets[i].Secret;
					}
					statsTableEditor.LoadData(response.records);
				}
				else
					statsTableEditor.LoadData(response.error);
			}, function (jqXHR, textStatus, errorThrown)
				{
					statsTableEditor.LoadData(jqXHR.ErrorMessageHtml, true);
				});
		}
	}

	function statsRowClick(e)
	{
		var $tr = $(this);
		var recordId = parseInt($tr.attr("pk"));
		ExecAPI("getUsageRecords?detailsForId=" + recordId, function (response)
		{
			if (response.result == "success")
				showModal("Details of Usage Record", MakeUsageRecordDetails(response));
			else
				SimpleDialog.html("Details of Usage Record - ERROR<br>" + response.error, modalOptions);
		}, function (jqXHR, textStatus, errorThrown)
			{
				SimpleDialog.html("Details of Usage Record - ERROR<br>" + jqXHR.ErrorMessageHtml, modalOptions);
			});
	}

	function MakeUsageRecordDetails(response)
	{
		var u = response.usage;
		if (u.CameraCount == 0)
			u.CameraCount = 1;
		var sb = [];
		if (isAdmin)
		{
			sb.push('<h3 class="text-center">Admin</h3>');
			sb.push('<dl class="dl-horizontal">');
			sb.push(MakeDLRow("Record ID", u.ID));
			sb.push(MakeDLRow("Secret String", response.secret));
			sb.push(MakeDLRowHtml("", '<button type="button" class="btn btn-danger" onclick="siteApi.DeleteRecord(' + u.ID + ')">Delete Record</button>'));
			sb.push('</dl>');
		}
		sb.push('<h3 class="text-center">System Information</h3>');
		sb.push('<dl class="dl-horizontal">');
		sb.push(MakeDLRow("Record Date", new Date(u.Timestamp).toLocaleString()));
		sb.push(MakeDLRow("Age of Record", msToTimeString(Date.now() - u.Timestamp)));
		sb.push(MakeDLRow("Operating System", u.OS));
		sb.push(MakeDLRow("Blue Iris Version", u.BiVersion));
		sb.push(MakeDLRow("Helper Version", u.HelperVersion));
		sb.push('</dl>');
		sb.push('<h3 class="text-center">CPU</h3>');
		sb.push('<dl class="dl-horizontal">');
		sb.push(MakeDLRow("Model", u.CpuModel));
		sb.push(MakeDLRow("Clock Speed", u.CpuMHz + " MHz"));
		sb.push(MakeDLRow("Usage (Overall)", u.CpuUsage + "%"));
		sb.push(MakeDLRow("Usage (Blue Iris)", u.BiCpuUsage + "%"));
		sb.push('</dl>');
		sb.push('<h3 class="text-center">Memory</h3>');
		sb.push('<dl class="dl-horizontal">');
		sb.push(MakeDLRow("Physical Capacity", u.RamGiB + " GiB"));
		sb.push(MakeDLRow("Memory Channels", (u.RamChannels == 0 ? "Unknown" : u.RamChannels)));
		sb.push(MakeDLRow("RAM Speed", u.RamMHz + " MHz"));
		sb.push(MakeDLRow("System Memory", MB_To_MiB(u.MemMB, 1) + " MiB"));
		sb.push(MakeDLRow("Memory (Free)", MB_To_MiB(u.MemFreeMB, 1) + " MiB"));
		sb.push(MakeDLRow("Memory (Blue Iris)", MB_To_MiB(u.BiMemUsageMB, 1) + " MiB"));
		sb.push(MakeDLRow("Peak Virtual (BI)", MB_To_MiB(u.BiPeakVirtualMemUsageMB, 1) + " MiB"));
		sb.push('</dl>');
		sb.push('<h3 class="text-center">Camera Overview</h3>');
		sb.push('<dl class="dl-horizontal">');
		sb.push(MakeDLRow("Hardware Acceleration", u.HwAccel));
		sb.push(MakeDLRow("Megapixels / Second", u.Total_MPPS.toFixedLoose(1) + " MP/s"));
		sb.push(MakeDLRow("Camera Count", u.CameraCount));
		sb.push(MakeDLRow("Total Megapixels", u.Total_Megapixels.toFixedLoose(1) + " MP"));
		sb.push(MakeDLRow("Average Megapixels", (u.Total_Megapixels / u.CameraCount).toFixedLoose(1) + " MP"));
		sb.push(MakeDLRow("Total FPS", u.Total_FPS + " FPS"));
		sb.push(MakeDLRow("Average FPS", (u.Total_FPS / u.CameraCount).toFixedLoose(1) + " FPS"));
		sb.push('</dl>');
		sb.push('<h3 class="text-center">GPU Details</h3>');
		if (response.gpus.length > 0)
		{
			sb.push('<table class="table">');
			sb.push('<thead><tr><th>Name</th><th>Driver Version</th></thead>');
			sb.push('<tbody>');
			for (var i = 0; i < response.gpus.length; i++)
			{
				var g = response.gpus[i];
				sb.push('<tr><td>' + EscapeHTML(g.Name) + '</td><td>' + EscapeHTML(g.DriverVersion) + '</td></tr>');
			}
			sb.push('</tbody>');
			sb.push('</table>');
		}
		else
			sb.push('<p>Unavailable</p>');
		sb.push('<h3 class="text-center">Camera Details</h3>');
		if (response.cameras.length > 0)
		{
			sb.push('<table class="table">');
			sb.push('<thead><tr><th>Type</th><th>Megapixels</th><th>FPS</th><th>Hardware Acceleration</th><th>Limit Decode</th><th>Motion Detection</th><th>Recording Trigger</th><th>Format</th><th>Direct-to-disc</th><th>Video Codec</th></tr></thead>');
			sb.push('<tbody>');
			for (var i = 0; i < response.cameras.length; i++)
			{
				var c = response.cameras[i];
				var type = c.Type == "ScreenCapture" ? (c.Type + " (" + c.ScreenCapType + ")") : c.Type;
				sb.push('<tr>');
				sb.push('<td>' + type + '</td>');
				sb.push('<td>' + (c.Pixels / 1000000).toFixedLoose(1) + '</td>');
				sb.push('<td>' + c.FPS + '</td>');
				sb.push('<td>' + c.Hwaccel + '</td>');
				sb.push('<td>' + c.LimitDecode + '</td>');
				sb.push('<td>' + c.MotionDetector + '</td>');
				sb.push('<td>' + c.RecordTriggerType + '</td>');
				sb.push('<td>' + c.RecordFormat + '</td>');
				sb.push('<td>' + c.DirectToDisk + '</td>');
				if (c.DirectToDisk || c.RecordFormat == "WMV")
					sb.push('<td>N/A</td>');
				else
					sb.push('<td>' + EscapeHTML(c.VCodec) + '</td>');
				sb.push('</tr>');
			}
			sb.push('</tbody>');
			sb.push('</table>');
		}
		else
			sb.push('<p>Unavailable</p>');
		return sb.join("");
	}
	function MakeDLRow(titleHtml, contentText)
	{
		return '<dt>' + titleHtml + '</dt><dd>' + EscapeHTML(contentText) + '</dd>';
	}
	function MakeDLRowHtml(titleHtml, contentHtml)
	{
		return '<dt>' + titleHtml + '</dt><dd>' + contentHtml + '</dd>';
	}
	function showModal(title, htmlOrEleContent)
	{
		var $modal = $('<div class="modal" id="myModal" tabindex="-1" role="dialog" aria-labelledby="myModalLabel">'
			+ '  <div class="modal-dialog modal-lg" role="document">'
			+ '    <div class="modal-content">'
			+ '      <div class="modal-header">'
			+ '        <button type="button" class="close" data-dismiss="modal" aria-label="Close"><span aria-hidden="true">&times;</span></button>'
			+ '        <h4 class="modal-title" id="myModalLabel"></h4>'
			+ '      </div>'
			+ '      <div class="modal-body">'
			+ '      </div>'
			+ '      <div class="modal-footer">'
			+ '        <button type="button" class="btn btn-default" data-dismiss="modal">Close</button>'
			+ '      </div>'
			+ '    </div>'
			+ '  </div>'
			+ '</div>')
		$modal.find("#myModalLabel").append(title);
		$modal.find(".modal-body").append(htmlOrEleContent);
		$('body').append($modal);
		$modal.modal();
	}
	function RenderPercent(item, editable, fieldName)
	{
		return item[fieldName] + "%";
	}
	function RenderSecret(item, editable, fieldName)
	{
		if (statsResponse.secretsMap)
		{
			var secret = statsResponse.secretsMap[item.ID];
			if (secret)
				return secret.substr(0, 8);
		}
		return "";
	}
	function RenderAge(item, editable, fieldName)
	{
		return msToRoughTimeString(Date.now() - item.Timestamp)
	}
	function RenderMBtoMiB(item, editable, fieldName)
	{
		return MB_To_MiB(item[fieldName], 0) + " MiB";
	}
	function RenderMEM(item, editable, fieldName)
	{
		return MB_To_MiB(item.MemFreeMB, 0) + " MiB / " + MB_To_MiB(item.MemMB, 0) + " MiB";
	}
	function RenderRAM(item, editable, fieldName)
	{
		return (item.RamChannels == 0 ? "~" : item.RamChannels) + "/" + item.RamMHz;
	}
	function RenderDec1(item, editable, fieldName)
	{
		return item[fieldName].toFixedLoose(1);
	}
	function DeleteRecord(ID)
	{
		SimpleDialog.ConfirmText("Are you sure you want to delete record " + ID + "?", function ()
		{
			ExecAPI("deleteRecord?id=" + ID, function (response)
			{
				if (response.result == "success")
					SimpleDialog.text("Record " + ID + " Deleted", modalOptions);
				else
					SimpleDialog.html("Delete Record - ERROR<br>" + response.error, modalOptions);
			}, function (jqXHR, textStatus, errorThrown)
				{
					SimpleDialog.html("Delete Record - ERROR<br>" + jqXHR.ErrorMessageHtml, modalOptions);
				});
		}, null, modalOptions);
	}
	Number.prototype.toFixedLoose = function (decimals)
	{
		return parseFloat(this.toFixed(decimals));
	}
	function MB_To_MiB(MB, fixedPrecision)
	{
		var B = MB * 1000000;
		var MiB = B / 1048576;
		if (typeof fixedPrecision == "number")
			return MiB.toFixed(fixedPrecision);
		else
			return MiB;
	}
	function msToTimeString(totalMs)
	{
		var ms = totalMs % 1000;
		var totalS = totalMs / 1000;
		var totalM = totalS / 60;
		var totalH = totalM / 60;
		var totalD = totalH / 24;
		//var s = Math.floor(totalS) % 60;
		var m = Math.floor(totalM) % 60;
		var h = Math.floor(totalH) % 24;
		var d = Math.floor(totalD);

		var retVal = "";
		if (d != 0)
			retVal += d + " day" + (d == 1 ? "" : "s") + ", ";
		if (d != 0 || h != 0)
			retVal += h + " hour" + (h == 1 ? "" : "s") + ", ";
		retVal += m + " minute" + (m == 1 ? "" : "s");
		return retVal;
	}
	function msToRoughTimeString(totalMs)
	{
		var ms = totalMs % 1000;
		var totalS = totalMs / 1000;
		var totalM = totalS / 60;
		var totalH = totalM / 60;
		var totalD = totalH / 24;
		var s = Math.floor(totalS) % 60;
		var m = Math.floor(totalM) % 60;
		var h = Math.floor(totalH) % 24;
		var d = Math.floor(totalD);

		if (d != 0)
			return d + " day" + (d == 1 ? "" : "s");
		if (h != 0)
			return h + " hour" + (h == 1 ? "" : "s");
		if (m != 0)
			return m + " minute" + (m == 1 ? "" : "s");
		return s + " second" + (s == 1 ? "" : "s");
	}
	var escape = document.createElement('textarea');
	var EscapeHTML = function (html)
	{
		escape.textContent = html;
		return escape.innerHTML;
	}
	var UnescapeHTML = function (html)
	{
		escape.innerHTML = html;
		return escape.textContent;
	}
	function ExecAPI(cmd, callbackSuccess, callbackFail)
	{
		var reqUrl = "api/" + cmd;
		$.ajax({
			type: 'POST',
			url: reqUrl,
			contentType: "text/plain",
			data: "",
			dataType: "json",
			success: function (data)
			{
				if (callbackSuccess)
					callbackSuccess(data);
			},
			error: function (jqXHR, textStatus, errorThrown)
			{
				if (!jqXHR)
					jqXHR = { status: 0, statusText: "No jqXHR object was created" };
				jqXHR.OriginalURL = reqUrl;
				jqXHR.ErrorMessageHtml = 'Response: ' + jqXHR.status + ' ' + jqXHR.statusText + '<br>Status: ' + textStatus + '<br>Error: ' + errorThrown + '<br>URL: ' + reqUrl;
				if (callbackFail)
					callbackFail(jqXHR, textStatus, errorThrown);
			}
		});
	}
	return {
		DeleteRecord: DeleteRecord
	};
})(jQuery);