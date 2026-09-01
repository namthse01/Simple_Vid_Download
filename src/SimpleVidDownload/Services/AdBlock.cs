using System.Text.RegularExpressions;

namespace SimpleVidDownload.Services;

/// <summary>
/// Chặn quảng cáo cho trình duyệt nhúng: chặn ở tầng mạng, ẩn khung quảng cáo còn sót,
/// và vô hiệu hoá trò mở cửa sổ ngầm (popunder).
///
/// Luật do mình tự viết, cố tình để hẹp. Danh sách lọc kiểu EasyList tuy chặn sạch hơn
/// nhưng nặng, hay chặn nhầm, và có ràng buộc giấy phép — không đáng cho một cửa sổ
/// chỉ mở vài phút để lấy link.
///
/// Ưu tiên số một: KHÔNG bao giờ chặn nhầm chính video, vì chặn nhầm là hỏng cả tính năng.
/// </summary>
public static class AdBlock
{
    /// <summary>Host quảng cáo/theo dõi — chắc chắn tới mức chặn được cả khung iframe.</summary>
    private static readonly Regex AdHost = new(
        @"(^|\.)("
        + @"doubleclick\.net|googlesyndication\.com|googleadservices\.com|adservice\.google\.\w+|"
        + @"adnxs\.com|rubiconproject\.com|pubmatic\.com|openx\.net|criteo\.(com|net)|"
        + @"popads\.net|popcash\.net|poptm\.com|propellerads\.com|propu\.sh|"
        + @"exoclick\.com|exosrv\.com|realsrv\.com|juicyads\.com|trafficjunky\.(net|com)|"
        + @"trafficfactory\.biz|adsterra\.com|hilltopads\.net|clickadu\.com|adcash\.com|"
        + @"ad-maven\.com|onclickalgo\.com|onclickperformance\.com|zeropark\.com|"
        + @"mgid\.com|taboola\.com|outbrain\.com|revcontent\.com|bidvertiser\.com|"
        + @"adform\.net|smartadserver\.com|adroll\.com|scorecardresearch\.com|"
        + @"vietadx\.\w+|admicro\.vn|adtima\.vn"
        + @")",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// Đường dẫn kiểu quảng cáo. Chỉ dùng cho tài nguyên phụ (ảnh, script), KHÔNG dùng cho
    /// trang chính — trang thật mà trùng chữ là chặn oan cả trang.
    /// Cố ý đòi dấu / ở đầu: "/ads/" thì chặn, còn "/uploads/" hay "/downloads/" thì tha.
    /// </summary>
    private static readonly Regex AdPath = new(
        @"/(ads|adserver|adframe|adservice|advert|adverts|advertising|advertisement|"
        + @"popunder|popads|banners)/|"
        + @"/(ads|adsense|popunder|popup|banner)\.(js|php|html?)(\?|$)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// Có phải thứ đáng chặn không.
    /// </summary>
    /// <param name="url">Địa chỉ của request.</param>
    /// <param name="isDocument">Có phải request cho cả một trang/khung không.</param>
    /// <param name="isTopLevel">Có phải chính trang người dùng đang mở không.</param>
    public static bool ShouldBlock(string url, bool isDocument, bool isTopLevel)
    {
        if (string.IsNullOrEmpty(url)) return false;

        // Trang người dùng tự gõ thì không bao giờ chặn, kể cả tên miền trông giống quảng cáo.
        if (isTopLevel) return false;

        // Chính video thì miễn — thà lọt quảng cáo còn hơn mất link cần lấy.
        if (MediaSniffer.KindFromUrl(url) is not null) return false;

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return false;
        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) return false;

        if (AdHost.IsMatch(uri.Host)) return true;

        // Luật theo đường dẫn lỏng hơn nên chỉ áp cho tài nguyên phụ.
        return !isDocument && AdPath.IsMatch(uri.AbsolutePath);
    }

    /// <summary>
    /// Chạy trước mọi script của trang. Ba việc: chặn mở cửa sổ ngầm, ẩn khung quảng cáo
    /// còn sót, và gỡ lớp phủ trong suốt nuốt cú bấm đầu tiên.
    /// </summary>
    public const string PageScript = """
        (function () {
          'use strict';

          // Script nay chay TRUOC khi trang co phan tu nao: document.head va ca
          // documentElement deu con null. Nen moi viec dung DOM phai tu bao ve, va
          // phai dang ky su kien TRUOC khi thu chay lan dau — khong thi mot loi o
          // lan chay som se keo sap toan bo phan con lai.

          try {
            window.open = function () { return null; };
          } catch (e) {}

          var css =
            'iframe[src*="doubleclick"],iframe[src*="googlesyndication"],iframe[src*="adservice"],' +
            'iframe[src*="exoclick"],iframe[src*="juicyads"],iframe[src*="popads"],' +
            'iframe[src*="adsterra"],iframe[src*="trafficjunky"],' +
            '.adsbygoogle,ins.adsbygoogle,[id^="google_ads"],[id^="div-gpt-ad"],' +
            '[id*="banner-ad"],[class*="banner-ad"],[class*="ad-banner"],' +
            '.ad-container,.ad-wrapper,.ads-container,.advertisement' +
            '{display:none !important;}';

          var adRx = /(doubleclick|googlesyndication|googleadservices|adservice\.google|adnxs|exoclick|exosrv|juicyads|popads|popcash|poptm|trafficjunky|adsterra|hilltopads|propellerads|clickadu|adcash|mgid|taboola|outbrain|revcontent|admicro|adtima|\/ads\/|\/advert)/i;

          // Gan bang an quang cao. Goi lai nhieu lan cung khong sao.
          var gan = function () {
            try {
              if (document.querySelector('style[data-dcd]')) return;
              var noi = document.head || document.documentElement;
              if (!noi) return;                       // trang chua dung xong
              var style = document.createElement('style');
              style.setAttribute('data-dcd', '1');
              style.textContent = css;
              noi.appendChild(style);
            } catch (e) {}
          };

          // Chan o tang mang roi thi the van nam do: anh vo, khung rong. Don thang.
          var donThe = function () {
            try {
              var the = document.querySelectorAll('img[src],iframe[src],ins,embed[src]');
              for (var j = 0; j < the.length; j++) {
                var el = the[j];
                var src = el.getAttribute('src') || '';
                if (src && adRx.test(src)) { el.remove(); continue; }
                if (el.tagName === 'INS' && /adsbygoogle/i.test(el.className || '')) el.remove();
              }
            } catch (e) {}
          };

          // Lop phu trong suot phu kin man hinh de nuot cu bam dau tien roi mo quang cao.
          // Chi go thu RONG va phu gan kin: co video/anh/chu ben trong la cua trang, de yen.
          var goLopPhu = function () {
            try {
              var vw = window.innerWidth, vh = window.innerHeight;
              if (!vw || !vh || !document.body) return;
              var nodes = document.querySelectorAll('a,div');
              for (var i = 0; i < nodes.length; i++) {
                var n = nodes[i];
                if (n.querySelector('video,iframe,img,canvas,button,input')) continue;
                if ((n.textContent || '').trim().length) continue;
                var st = window.getComputedStyle(n);
                if (st.position !== 'fixed' && st.position !== 'absolute') continue;
                if ((parseInt(st.zIndex, 10) || 0) < 100) continue;
                var r = n.getBoundingClientRect();
                if (r.width >= vw * 0.8 && r.height >= vh * 0.8) n.remove();
              }
            } catch (e) {}
          };

          var quet = function () { gan(); donThe(); goLopPhu(); };

          // Dang ky TRUOC, chay sau.
          try { document.addEventListener('DOMContentLoaded', quet); } catch (e) {}
          try {
            var lan = 0;
            var hen = setInterval(function () { quet(); if (++lan >= 12) clearInterval(hen); }, 1000);
          } catch (e) {}
          quet();
        })();
        """;
}
