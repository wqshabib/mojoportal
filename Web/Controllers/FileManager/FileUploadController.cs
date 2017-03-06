﻿using log4net;
using mojoPortal.Business;
using mojoPortal.Business.WebHelpers;
using mojoPortal.FileSystem;
using mojoPortal.Web.Framework;
using mojoPortal.Web.Models;
using Resources;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http.Headers;
using System.Text;
using System.Text.RegularExpressions;
using System.Web;
using System.Web.Http;

namespace mojoPortal.Web.Controllers.FileManager
{
	public class FileUploadController : ApiController
	{
		IFileSystem fileSystem = null;
		private string virtualPath = string.Empty;
		private static readonly ILog log = LogManager.GetLogger(typeof(FileServiceController));

		[HttpPost]
		public FileService.ReturnObject UploadFiles()
		{
			FileSystemProvider p = FileSystemManager.Providers[WebConfigSettings.FileSystemProvider];
			fileSystem = p.GetFileSystem();
			virtualPath = fileSystem.VirtualRoot;
			var context = HttpContext.Current;
			HttpFileCollection files = context.Request.Files.Count > 0 ? context.Request.Files : null;
			OpResult results = OpResult.Error;
			StringBuilder errors = new StringBuilder();
			SiteSettings siteSettings = CacheHelper.GetCurrentSiteSettings();
			string uploadPath = virtualPath;
			bool canUpload = (
				WebUser.IsAdminOrContentAdmin ||
				SiteUtils.UserIsSiteEditor() ||
				WebUser.IsInRoles(siteSettings.GeneralBrowseAndUploadRoles) ||
				WebUser.IsInRoles(siteSettings.UserFilesBrowseAndUploadRoles) ||
				WebUser.IsInRoles(siteSettings.RolesThatCanDeleteFilesInEditor)
			);

			if (files.Count == 0)
			{
				log.Info("No files posted so returning 404");
				return new FileService.ReturnObject(new FileService.ReturnMessage { Success = false, Error = "No files posted so returning 404" });
			}

			if (WebConfigSettings.DisableFileManager)
			{
				log.Info("File Manager disabled by web.config");
				return new FileService.ReturnObject(new FileService.ReturnMessage { Success = false, Error = "File Manager disabled by web.config" });
			}

			if (fileSystem == null)
			{
				log.Info("FileSystem is null so returning 404");
				return new FileService.ReturnObject(new FileService.ReturnMessage { Success = false, Error = "FileSystem is null so returning 404" });
			}

			if (siteSettings == null)
			{
				return new FileService.ReturnObject(new FileService.ReturnMessage { Success = false, Error = "No site settings" });
			}

			if (!canUpload)
			{
				return new FileService.ReturnObject(new FileService.ReturnMessage { Success = false, Error = Resource.AccessDenied });
			}

			if (context.Request.Form.Get("destination") != null)
			{
				uploadPath = FilePath(VirtualPathUtility.AppendTrailingSlash(context.Request.Form.Get("destination")));
			}

			if (files != null)
			{
				var fileUploadsRemaining = fileSystem.Permission.MaxFiles - fileSystem.CountAllFiles();

				if (fileUploadsRemaining < files.Count)
				{
					string errorMessage = string.Empty;
					log.Info("upload rejected due to fileSystem.Permission.MaxFiles");

					if (fileUploadsRemaining == 0)
					{
						errorMessage = Resource.FileSystemFileLimitReached;
					}
					else
					{
						errorMessage = string.Format(Resource.FileSystemFileLimitRemainder, fileUploadsRemaining);
					}

					return new FileService.ReturnObject(new FileService.ReturnMessage { Success = false, Error = errorMessage });
				}

				for (int f = 0; f < files.Count; f++)
				{
					HttpPostedFile file = files[f];
					bool doUpload = true;

					if (file.ContentLength > fileSystem.Permission.MaxSizePerFile)
					{
						log.Info("upload rejected due to fileSystem.Permission.MaxSizePerFile");
						errors.AppendLine(Resource.FileSystemFileTooLargeError);
						doUpload = false;
					}
					else if (fileSystem.GetTotalSize() + file.ContentLength >= fileSystem.Permission.Quota)
					{
						log.Info("upload rejected due to fileSystem.Permission.Quota");
						errors.AppendLine(Resource.FileSystemStorageQuotaError);
						doUpload = false;
					}

					if (!fileSystem.Permission.IsExtAllowed(VirtualPathUtility.GetExtension(file.FileName)))
					{
						log.Info("upload rejected due to not allowed file extension");
						errors.AppendLine(Resource.FileTypeNotAllowed);
						doUpload = false;
					}

					string destPath = VirtualPathUtility.Combine(uploadPath, Path.GetFileName(file.FileName).ToCleanFileName(WebConfigSettings.ForceLowerCaseForUploadedFiles));

					string ext = Path.GetExtension(file.FileName);
					string mimeType = file.ContentType;

					if (doUpload)
					{
						using (Stream s = file.InputStream)
						{
							results = fileSystem.SaveFile(destPath, s, mimeType, true);
						}

						if (results != OpResult.Succeed)
						{
							errors.AppendLine(results.ToString());
						}
					}
				}

				if (errors.Length > 0)
				{
					return new FileService.ReturnObject(new FileService.ReturnMessage { Success = false, Error = errors.ToString() });
				}

				return new FileService.ReturnObject(new FileService.ReturnMessage { Success = true });
			}
			else
			{
				return new FileService.ReturnObject(new FileService.ReturnMessage { Success = false, Error = Resource.NoFileSelectedWarning });
			}
		}

		private string FilePath(string itemPath, bool returnDiskPath = false, bool appendTrailingSlash = false)
		{
			Regex removeDoubleSlash = new Regex("(/)(?<=\\1\\1)");

			if (string.IsNullOrEmpty(itemPath))
			{
				return itemPath;
			}

			// Remove "../" or "\" to prevent hacks 
			itemPath = itemPath.Replace("..", string.Empty).Replace("\\", string.Empty).Trim();
			string concatedPath = virtualPath + itemPath;
			// Clean virtual path
			string cleanPath = removeDoubleSlash.Replace(concatedPath, String.Empty);
			if (appendTrailingSlash)
				cleanPath = VirtualPathUtility.AppendTrailingSlash(cleanPath);
			// Clean disk path
			string discPath = HttpContext.Current.Server.MapPath(cleanPath);

			if (returnDiskPath)
				return discPath;
			else
				return cleanPath;
		}
	}
}
