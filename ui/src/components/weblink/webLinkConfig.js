// Shared between Dashboard.vue (editable) and PublicView.vue (read-only) so both
// render a Web Link widget's icon and URL identically -- widget JSON only stores
// the icon's *name* (icons aren't serializable), so this map is the single source
// of truth translating that name back into an actual component in either place.
import {
    Link, Globe, ExternalLink, Github, Mail, FileText, Home, Info, BookOpen,
    Video, Image as ImageIcon, Database, BarChart2, Settings, Star, Bookmark,
    Cloud, ShoppingCart, Phone, MapPin, Calendar, Users, Briefcase, Newspaper,
    Youtube, Twitter, Facebook, Linkedin, MessageCircle, Slack
} from 'lucide-vue-next';

export const webLinkIconOptions = [
    { name: 'Link', icon: Link },
    { name: 'Globe', icon: Globe },
    { name: 'ExternalLink', icon: ExternalLink },
    { name: 'Github', icon: Github },
    { name: 'Mail', icon: Mail },
    { name: 'FileText', icon: FileText },
    { name: 'Home', icon: Home },
    { name: 'Info', icon: Info },
    { name: 'BookOpen', icon: BookOpen },
    { name: 'Video', icon: Video },
    { name: 'Image', icon: ImageIcon },
    { name: 'Database', icon: Database },
    { name: 'BarChart2', icon: BarChart2 },
    { name: 'Settings', icon: Settings },
    { name: 'Star', icon: Star },
    { name: 'Bookmark', icon: Bookmark },
    { name: 'Cloud', icon: Cloud },
    { name: 'ShoppingCart', icon: ShoppingCart },
    { name: 'Phone', icon: Phone },
    { name: 'MapPin', icon: MapPin },
    { name: 'Calendar', icon: Calendar },
    { name: 'Users', icon: Users },
    { name: 'Briefcase', icon: Briefcase },
    { name: 'Newspaper', icon: Newspaper },
    { name: 'Youtube', icon: Youtube },
    { name: 'Twitter', icon: Twitter },
    { name: 'Facebook', icon: Facebook },
    { name: 'Linkedin', icon: Linkedin },
    { name: 'MessageCircle', icon: MessageCircle },
    { name: 'Slack', icon: Slack }
];

const iconLookup = Object.fromEntries(webLinkIconOptions.map((o) => [o.name, o.icon]));

export function getWebLinkIcon(name) {
    return iconLookup[name] || Link;
}

// Only allow schemes that are safe to navigate to. Anything unrecognized is
// treated as a bare domain (prefixed with https://); anything explicitly
// dangerous (javascript:/data:/vbscript:, which could run script in a viewer's
// authenticated session when clicked) is rejected outright rather than "fixed".
const SAFE_SCHEME = /^(https?:|mailto:|tel:)/i;
const DANGEROUS_SCHEME = /^(javascript|data|vbscript|file):/i;

export function sanitizeWebLinkUrl(url) {
    if (!url) return '';
    const trimmed = String(url).trim();
    if (!trimmed) return '';
    if (SAFE_SCHEME.test(trimmed)) return trimmed;
    if (DANGEROUS_SCHEME.test(trimmed)) return '';
    return `https://${trimmed}`;
}
