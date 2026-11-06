import { CotesPhoto, QualitesJpeg, TailleMaxPhoto, dimensionsReduites } from './passage.ts'

/**
 * La photo d'une anomalie, réduite en JPEG sous `TailleMaxPhoto` (SPEC.md, étape 25).
 *
 * Hors de `passage.ts` parce qu'elle exige un navigateur — un canevas, un décodeur
 * d'image — et que `node --test` n'en a pas. Ce qui s'y décide sans canevas, les côtés
 * et les qualités essayés, est dans `passage.ts`, éprouvé.
 *
 * Le réencodage par le canevas ne recopie aucune métadonnée : la position GPS de la
 * prise ne quitte donc pas le téléphone. ⚠️ C'est une minimisation, pas la garde — le
 * serveur retire les métadonnées de tout ce qu'il reçoit, une requête forgée comprise.
 */
export async function reduirePhoto(fichier: Blob): Promise<Blob> {
  // `from-image` redresse une photo prise téléphone tourné : l'orientation est une
  // métadonnée, que le réencodage perdrait sinon, et la photo arriverait couchée.
  const image = await createImageBitmap(fichier, { imageOrientation: 'from-image' })

  try {
    for (const cote of CotesPhoto) {
      const { largeur, hauteur } = dimensionsReduites(image.width, image.height, cote)
      const toile = document.createElement('canvas')

      toile.width = largeur
      toile.height = hauteur

      const contexte = toile.getContext('2d')
      if (contexte === null) throw new Error('Ce téléphone ne sait pas préparer la photo.')

      contexte.drawImage(image, 0, 0, largeur, hauteur)

      for (const qualite of QualitesJpeg) {
        const jpeg = await enJpeg(toile, qualite)

        if (jpeg.size <= TailleMaxPhoto) return jpeg
      }
    }
  } finally {
    image.close()
  }

  throw new Error('Photo trop lourde, même réduite. En prendre une autre.')
}

function enJpeg(toile: HTMLCanvasElement, qualite: number): Promise<Blob> {
  return new Promise((resoudre, rejeter) => {
    toile.toBlob(
      (blob) => {
        if (blob === null) rejeter(new Error('Ce téléphone ne sait pas préparer la photo.'))
        else resoudre(blob)
      },
      'image/jpeg',
      qualite,
    )
  })
}
